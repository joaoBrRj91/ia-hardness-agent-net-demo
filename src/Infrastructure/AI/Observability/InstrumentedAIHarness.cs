using System.Diagnostics;
using Domain.AI.Harness;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.Observability;

/// <summary>
/// Decorator de IAIHarness — span raiz "harness.process" + métricas RED + custo.
/// O harness interno não sabe que é observado (telemetria é cross-cutting concern).
/// Tokens/modelo vêm do TokenUsageAccumulator (mesmo scope), alimentado pelos
/// call sites de ILLMClient.
/// </summary>
public sealed class InstrumentedAIHarness : IAIHarness
{
    private readonly IAIHarness           _inner;
    private readonly AIHarnessMetrics     _metrics;
    private readonly ITelemetryRedactor   _redactor;
    private readonly TokenUsageAccumulator _usage;
    private readonly TelemetryOptions     _options;
    private readonly ILogger<InstrumentedAIHarness> _logger;

    public InstrumentedAIHarness(
        IAIHarness            inner,
        AIHarnessMetrics      metrics,
        ITelemetryRedactor    redactor,
        TokenUsageAccumulator usage,
        IOptions<TelemetryOptions> options,
        ILogger<InstrumentedAIHarness> logger)
    {
        _inner    = inner;
        _metrics  = metrics;
        _redactor = redactor;
        _usage    = usage;
        _options  = options.Value;
        _logger   = logger;
    }

    public async Task<HarnessResponse> ProcessAsync(HarnessRequest request, CancellationToken ct = default)
    {
        // Kind=Client: backend calcula latência de rede a partir disso.
        // 'using' obrigatório: restaura Activity.Current no Dispose.
        using var activity = AIDiagnostics.Source.StartActivity("harness.process", ActivityKind.Client);

        using var active = _metrics.TrackActive(_options.DefaultModel);
        var sw = Stopwatch.StartNew();

        // Alta cardinalidade → span attribute, JAMAIS tag de métrica
        activity?.SetTag(GenAiConventions.System, "anthropic");
        activity?.SetTag(GenAiConventions.OperationName, "chat");
        activity?.SetTag(GenAiConventions.TenantId, request.Context.TenantId);
        activity?.SetTag(GenAiConventions.UserId, request.Context.UserId);

        if (_options.RecordContent)
            RecordContentEvent(activity, GenAiConventions.EventPrompt, request.Input);

        try
        {
            var response = await _inner.ProcessAsync(request, ct);
            sw.Stop();

            var model = _usage.PrimaryModel ?? _options.DefaultModel;
            var cost  = EstimateCost();

            activity?.SetTag(GenAiConventions.ResponseModel,  model);
            activity?.SetTag(GenAiConventions.InputTokens,    _usage.TotalInput);
            activity?.SetTag(GenAiConventions.OutputTokens,   _usage.TotalOutput);
            activity?.SetTag(GenAiConventions.IterationCount, response.IterationCount);
            activity?.SetTag(GenAiConventions.CorrelationId,  response.CorrelationId);
            activity?.SetTag(GenAiConventions.CostUsd,        cost);
            activity?.SetStatus(ActivityStatusCode.Ok);

            // ── CONTRATO COM O COLLECTOR ──
            // String explícita, sempre presente. bool não casa com string_attribute.
            // A aplicação MARCA; o Collector DECIDE.
            activity?.SetTag(GenAiConventions.WasFallback,
                response.WasFallback ? "true" : "false");
            activity?.SetTag(GenAiConventions.Anomalous,
                IsAnomalous(response, cost) ? "true" : "false");

            if (_options.RecordContent)
                RecordContentEvent(activity, GenAiConventions.EventCompletion, response.Output);

            _metrics.RecordOperation(sw.Elapsed.TotalSeconds, model, "ok",
                                     response.IterationCount, cost);
            _metrics.RecordTokens(_usage.TotalInput, _usage.TotalOutput, model);

            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();

            // SetStatus(Error) basta: a policy status_code do Collector lê nativamente.
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("error.type", ex.GetType().FullName);

            _metrics.RecordOperation(sw.Elapsed.TotalSeconds, _options.DefaultModel,
                                     ex.GetType().Name, 0, 0);

            // TraceId no log = correlação log↔trace com um clique na UI
            _logger.LogError(ex,
                "Harness failed | tenant={Tenant} traceId={TraceId} elapsed={Ms}ms",
                request.Context.TenantId, activity?.TraceId, sw.ElapsedMilliseconds);

            throw;
        }
    }

    /// <summary>
    /// Heurística de anomalia. Vive na APLICAÇÃO porque depende de semântica de
    /// domínio que o Collector não tem — mas a DECISÃO de reter fica no Collector,
    /// que pode mudar de política sem redeploy.
    /// </summary>
    private bool IsAnomalous(HarnessResponse r, double cost) =>
           r.WasFallback                            // caiu para o FallbackHandler
        || r.IterationCount >= 8                    // loop ReAct quase estourando
        || _usage.TotalInput > 100_000              // contexto anormalmente grande
        || cost > _options.CostAlertThresholdUsd;

    private void RecordContentEvent(Activity? activity, string eventName, string content)
    {
        if (activity is null) return;   // caminho NORMAL quando não há listener

        var safe = _redactor.Redact(content);
        if (safe.Length > _options.MaxContentLength)
            safe = safe[.._options.MaxContentLength] + "…[truncated]";

        activity.AddEvent(new ActivityEvent(eventName,
            tags: new ActivityTagsCollection { { "content", safe } }));
    }

    private double EstimateCost()
        => _usage.ByModel.Sum(kv =>
            _options.Pricing.TryGetValue(kv.Key, out var p)
                ? (double)(kv.Value.In  / 1_000_000m * p.InputPerMillion +
                           kv.Value.Out / 1_000_000m * p.OutputPerMillion)
                : 0);
}
