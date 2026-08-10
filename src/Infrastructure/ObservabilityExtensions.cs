using System.Diagnostics;
using Domain.AI.Harness;
using Infrastructure.AI.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Infrastructure;

public static class ObservabilityExtensions
{
    /// <summary>
    /// Registra telemetria do harness. DEVE ser chamado DEPOIS de AddAIHarness:
    /// Decorate exige o registro original de IAIHarness, e o Replace de
    /// IAgentDiagnostics troca o NoOp default pelo real.
    /// </summary>
    public static IServiceCollection AddAIObservability(
        this IServiceCollection services, IConfiguration config)
    {
        services.Configure<TelemetryOptions>(config.GetSection("AI:Telemetry"));

        // Singleton OBRIGATÓRIO: instrumentos de Meter devem ser criados UMA vez.
        // Se virar Scoped, cada request cria novos instrumentos → séries duplicadas.
        services.AddSingleton<AIHarnessMetrics>();
        services.AddSingleton<ITelemetryRedactor, PaymentDataRedactor>();
        services.Replace(ServiceDescriptor.Singleton<IAgentDiagnostics, AgentDiagnostics>());

        // Scrutor: preserva o lifetime do registro original de IAIHarness (Scoped)
        services.Decorate<IAIHarness, InstrumentedAIHarness>();

        var otelEndpoint = new Uri(config["Otel:Endpoint"] ?? "http://localhost:4317");

        services.AddOpenTelemetry()
            // Resource: atributos comuns a TODO span/métrica/log deste processo.
            // service.name é o que forma o mapa de serviços no backend.
            .ConfigureResource(r => r
                .AddService(
                    serviceName:    "psp-ai-harness",
                    serviceVersion: typeof(ObservabilityExtensions).Assembly
                                        .GetName().Version?.ToString() ?? "0.0.0")
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = config["ASPNETCORE_ENVIRONMENT"] ?? "local"
                }))

            .WithTracing(t => t
                // SEM esta linha, StartActivity retorna null e NADA é coletado.
                .AddSource(GenAiConventions.ActivitySourceName)

                .AddAspNetCoreInstrumentation()    // EXTRAI traceparent de entrada
                .AddHttpClientInstrumentation()    // INJETA traceparent na saída

                // AlwaysOn: exporta 100%. A decisão de amostragem é DELEGADA
                // ao Collector (tail_sampling), que precisa do trace COMPLETO
                // (com status final) para decidir. Head sampling aqui destruiria
                // 90% dos traces com erro ANTES do tail_sampling poder vê-los.
                // ParentBased: se o upstream já descartou, respeitamos.
                .SetSampler(new ParentBasedSampler(new AlwaysOnSampler()))

                .AddOtlpExporter(exporter =>
                {
                    exporter.Endpoint = otelEndpoint;
                    exporter.Protocol = OtlpExportProtocol.Grpc;

                    // Fila cheia = spans descartados SILENCIOSAMENTE.
                    // Com AlwaysOn o volume é 10× maior → 2048 default é insuficiente.
                    exporter.BatchExportProcessorOptions = new BatchExportProcessorOptions<Activity>
                    {
                        MaxQueueSize               = 16_384,
                        ScheduledDelayMilliseconds = 5_000,
                        MaxExportBatchSize         = 512
                    };
                }))

            .WithMetrics(m => m
                .AddMeter(GenAiConventions.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()       // GC, threadpool, alocação

                // CRÍTICO: buckets default vão até ~10s. LLM estoura isso
                // rotineiramente → p99 cai no bucket +Inf → p99 é ficção.
                .AddView("gen_ai.client.operation.duration",
                    new ExplicitBucketHistogramConfiguration
                    { Boundaries = [0.5, 1, 2, 4, 8, 16, 32, 64, 120] })

                .AddView("gen_ai.client.token.usage",
                    new ExplicitBucketHistogramConfiguration
                    { Boundaries = [100, 500, 1_000, 4_000, 16_000, 64_000, 200_000] })

                .AddView("psp.ai.iterations",
                    new ExplicitBucketHistogramConfiguration
                    { Boundaries = [1, 2, 3, 5, 8, 10] })

                .AddOtlpExporter((exporter, reader) =>
                {
                    exporter.Endpoint = otelEndpoint;
                    reader.PeriodicExportingMetricReaderOptions
                          .ExportIntervalMilliseconds = 60_000;
                }))

            // Correlação log↔trace: TraceId/SpanId injetados automaticamente em
            // todo log. WithLogging compartilha o Resource (service.name no Loki).
            .WithLogging(
                logging => logging.AddOtlpExporter(e => e.Endpoint = otelEndpoint),
                options =>
                {
                    options.IncludeScopes           = true;
                    options.IncludeFormattedMessage = true;
                });

        return services;
    }
}
