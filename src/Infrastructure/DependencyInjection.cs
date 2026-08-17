using Anthropic.SDK;
using Domain.AI.Agents;
using Domain.AI.Harness;
using Domain.AI.LLM;
using Domain.AI.RAG;
using Domain.AI.Routing;
using Domain.AI.Tools;
using Infrastructure.AI.Agents;
using Infrastructure.AI.Authorization;
using Infrastructure.AI.Harness;
using Infrastructure.AI.LLM;
using Infrastructure.AI.Mcp;
using Infrastructure.AI.Observability;
using Infrastructure.AI.RAG;
using Infrastructure.AI.Routing;
using Infrastructure.AI.Routing.Handlers;
using Infrastructure.AI.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAIHarness(
        this IServiceCollection services,
        IConfiguration          config)
    {
        // ── LLM Client ────────────────────────────────────────────────────
        if (config.GetValue("LLM:UseFake", defaultValue: false))
        {
            services.AddSingleton<FakeLLMClient>();
            services.AddSingleton<ILLMClient>(sp =>
            {
                var fake     = sp.GetRequiredService<FakeLLMClient>();
                var scenario = config["LLM:FakeScenario"];
                if (!string.IsNullOrWhiteSpace(scenario))
                    fake.LoadScenario(scenario);
                return fake;
            });
        }
        else
        {
            services.AddSingleton(_ =>
            {
                var apiKey = config["Anthropic:ApiKey"];
                if (string.IsNullOrWhiteSpace(apiKey))
                    apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
                if (string.IsNullOrWhiteSpace(apiKey))
                    throw new InvalidOperationException(
                        "Anthropic:ApiKey não configurada (defina em appsettings.json ou na variável ANTHROPIC_API_KEY)");
                return new AnthropicClient(new APIAuthentication(apiKey));
            });
            services.AddSingleton<ILLMClient, AnthropicLLMClient>();
        }

        // ── Módulo 3.2 — Authorization ────────────────────────────────────
        services.AddSingleton<IToolPolicy>(_ =>
        {
            var tenants = config.GetSection("AllowedTenants").Get<HashSet<string>>()
                ?? throw new InvalidOperationException("AllowedTenants não configurado");
            return new TenantIsolationPolicy(tenants);
        });
        services.AddSingleton<IToolPolicy, ReadOnlyPolicy>();
        services.AddSingleton<IToolPolicy>(_ =>
            new RateLimitPolicy(config.GetValue("RateLimit:ToolCallsPerMinute", 30)));
        services.AddSingleton<IToolAuthorizationService, ToolAuthorizationService>();

        // ── Módulo 3.1 — Tools ────────────────────────────────────────────
        services.AddSingleton<IToolDefinition, CheckTransactionStatusDefinition>();
        services.AddSingleton<IToolDefinition, IssueRefundDefinition>();
        services.AddScoped<IToolHandler, CheckTransactionStatusHandler>();
        services.AddScoped<IToolHandler, IssueRefundHandler>();
        services.AddSingleton<IToolRegistry>(sp =>
        {
            var definitions = sp.GetServices<IToolDefinition>().ToList();
            var logger      = sp.GetRequiredService<ILogger<ToolRegistry>>();
            return new ToolRegistryWithScopedHandlers(definitions, sp, logger);
        });

        // ── Módulo 2 — RAG Enricher ───────────────────────────────────────
        services.AddScoped<IPromptEnricher, RagPromptEnricher>();

        // ── Módulo 4.1 — Observabilidade + ReAct ─────────────────────────
        services.AddSingleton<IAgentObserver, LoggingAgentObserver>();
        services.AddScoped<ReActAgent>();

        // ── Módulo 4.2 — Reflection ───────────────────────────────────────
        services.AddScoped<ReflectionAgent>();

        // ── Módulo 4.3 — Routing Handlers ─────────────────────────────────
        services.AddScoped<IAgentHandler, InvestigateHandler>();
        services.AddScoped<IAgentHandler, AnalyzeHandler>();
        services.AddScoped<IAgentHandler, SummarizeHandler>();
        services.AddScoped<IAgentHandler, EscalateHandler>();
        services.AddScoped<FallbackHandler>();
        services.AddScoped<SemanticRouter>();

        // ── Módulo 4.4 — Harness Facade ───────────────────────────────────
        services.AddScoped<IAIHarness, AIHarness>();

        // ── Módulo 3.3 — MCP Client ───────────────────────────────────────
        services.AddScoped<McpHostService>();

        // ── Módulo 6.1 — hooks de observabilidade (defaults seguros) ─────
        // Agentes dependem de ambos; sem AddAIObservability o NoOp mantém
        // tudo funcional e o ActivitySource global quieto (testes paralelos).
        services.AddScoped<TokenUsageAccumulator>();
        services.TryAddSingleton<IAgentDiagnostics, NoOpAgentDiagnostics>();

        return services;
    }
}
