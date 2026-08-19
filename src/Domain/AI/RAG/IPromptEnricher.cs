namespace Domain.AI.RAG;

// Contrato definido no Módulo 2 — reproduzido para referência de composição.

public sealed record EnrichmentResult
{
    public required string  EnrichedInput  { get; init; }
    public required bool    WasEnriched    { get; init; }
    public          string? QueryUsed      { get; init; }
    public          int     ChunksInjected { get; init; }
}

public interface IPromptEnricher
{
    Task<EnrichmentResult> EnrichAsync(
        string            input,
        string            tenantId,
        CancellationToken ct = default);
}
