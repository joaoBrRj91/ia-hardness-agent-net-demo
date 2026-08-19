using Domain.AI.Agents;
using Domain.AI.Tools;

namespace Domain.AI.Harness;

public sealed record HarnessRequest
{
    public required string               Input   { get; init; }
    public required ToolExecutionContext Context { get; init; }

    /// <summary>
    /// Se fornecido, bypassa o SemanticRouter e força o intent.
    /// Útil para chamadas programáticas onde o intent é conhecido.
    /// </summary>
    public string? ForceIntent { get; init; }

    /// <summary>
    /// Propagado de sistemas upstream para distributed tracing.
    /// Gerado internamente se ausente.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Se true, desativa RAG enrichment.
    /// Usar quando o input já contém contexto suficiente.
    /// </summary>
    public bool SkipEnrichment { get; init; } = false;
}

public sealed record HarnessResponse
{
    public required string      Output          { get; init; }
    public required string      Intent          { get; init; }
    public required string      HandlerName     { get; init; }
    public required double      ConfidenceScore { get; init; }
    public required bool        WasFallback     { get; init; }
    public required string      TenantId        { get; init; }
    public required string      CorrelationId   { get; init; }
    public required long        DurationMs      { get; init; }

    // Hooks para Módulo 6 — OTel spans e métricas
    public int         IterationCount  { get; init; }
    public int         StepCount       { get; init; }
    public AgentStep[] AgentTrace      { get; init; } = [];
    public bool        WasEnriched     { get; init; }
    public string?     EnrichmentQuery { get; init; }
}

public interface IAIHarness
{
    Task<HarnessResponse> ProcessAsync(
        HarnessRequest    request,
        CancellationToken ct = default);
}
