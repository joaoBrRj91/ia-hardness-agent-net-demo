using System.Diagnostics;
using Domain.AI.Harness;
using Domain.AI.RAG;
using Domain.AI.Routing;
using Infrastructure.AI.Routing;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Harness;

/// <summary>
/// Facade que unifica Módulos 2, 3, 4.1, 4.2, 4.3 em um único entry point.
///
/// Pipeline por request:
///   1. CorrelationId → gera se ausente (distributed tracing)
///   2. Enrich        → RAG injeta contexto relevante no input
///   3. Route         → SemanticRouter ou ForceIntent bypass
///   4. Observe       → HarnessResponse coleta métricas (hook Módulo 6)
/// </summary>
public sealed class AIHarness : IAIHarness
{
    private readonly SemanticRouter     _router;
    private readonly IPromptEnricher    _enricher;
    private readonly ILogger<AIHarness> _logger;

    public AIHarness(
        SemanticRouter     router,
        IPromptEnricher    enricher,
        ILogger<AIHarness> logger)
    {
        _router   = router;
        _enricher = enricher;
        _logger   = logger;
    }

    public async Task<HarnessResponse> ProcessAsync(
        HarnessRequest    request,
        CancellationToken ct = default)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N")[..16];
        var sw            = Stopwatch.StartNew();

        _logger.LogInformation(
            "[Harness] START | correlationId={Id} tenant={Tenant} userId={User} forceIntent={Force}",
            correlationId, request.Context.TenantId, request.Context.UserId,
            request.ForceIntent ?? "none");

        try
        {
            // ── 1. RAG Enrichment ─────────────────────────────────────────
            EnrichmentResult enrichment;

            if (request.SkipEnrichment)
            {
                enrichment = new EnrichmentResult
                {
                    EnrichedInput  = request.Input,
                    WasEnriched    = false,
                    ChunksInjected = 0
                };
            }
            else
            {
                enrichment = await _enricher.EnrichAsync(
                    request.Input, request.Context.TenantId, ct);

                _logger.LogDebug(
                    "[Harness] Enrichment | wasEnriched={Was} chunks={Chunks} correlationId={Id}",
                    enrichment.WasEnriched, enrichment.ChunksInjected, correlationId);
            }

            // ── 2. ForceIntent bypass ou Semantic Routing ─────────────────
            RoutedResponse routedResponse;

            if (request.ForceIntent is not null)
            {
                var forcedDecision = new RouteDecision
                {
                    Intent     = request.ForceIntent,
                    Confidence = 1.0,
                    Reasoning  = "ForceIntent — bypass do SemanticRouter"
                };

                _logger.LogInformation(
                    "[Harness] ForceIntent={Intent} | correlationId={Id}",
                    request.ForceIntent, correlationId);

                routedResponse = await _router.RouteWithDecisionAsync(
                    enrichment.EnrichedInput, forcedDecision, request.Context, ct);
            }
            else
            {
                routedResponse = await _router.RouteAsync(
                    enrichment.EnrichedInput, request.Context, ct);
            }

            sw.Stop();

            // ── 3. HarnessResponse — coleta observabilidade ───────────────
            var response = new HarnessResponse
            {
                Output          = routedResponse.Output,
                Intent          = routedResponse.Intent,
                HandlerName     = routedResponse.HandlerName,
                ConfidenceScore = routedResponse.Confidence,
                WasFallback     = routedResponse.WasFallback,
                TenantId        = request.Context.TenantId,
                CorrelationId   = correlationId,
                DurationMs      = sw.ElapsedMilliseconds,
                WasEnriched     = enrichment.WasEnriched,
                EnrichmentQuery = enrichment.QueryUsed,
                IterationCount  = routedResponse.AgentTrace?.IterationCount ?? 0,
                StepCount       = routedResponse.AgentTrace?.Steps.Count    ?? 0,
                AgentTrace      = routedResponse.AgentTrace?.Steps.ToArray() ?? []
            };

            _logger.LogInformation(
                "[Harness] END | correlationId={Id} intent={Intent} handler={Handler} " +
                "confidence={Score:F2} fallback={Fallback} duration={Duration}ms steps={Steps}",
                correlationId, response.Intent, response.HandlerName, response.ConfidenceScore,
                response.WasFallback, response.DurationMs, response.StepCount);

            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "[Harness] ERROR | correlationId={Id} tenant={Tenant} duration={Duration}ms",
                correlationId, request.Context.TenantId, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
