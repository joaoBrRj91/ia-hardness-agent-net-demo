using System.Text;
using System.Text.RegularExpressions;
using Domain.AI.RAG;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.RAG;

/// <summary>
/// Módulo 2 — Enricher de prompt (RAG) simplificado.
/// Em produção: embeddings + vector store por tenant. Aqui usamos uma
/// knowledge base in-memory com recuperação por keyword para permitir
/// execução end-to-end sem infraestrutura externa.
/// </summary>
public sealed partial class RagPromptEnricher : IPromptEnricher
{
    private readonly ILogger<RagPromptEnricher> _logger;

    // PONTO DE EXTENSÃO — base de conhecimento estática do domínio.
    // Aqui ela é in-memory e por keyword só para tornar o pipeline demonstrável
    // offline; substitua pelos chunks do seu domínio (ou por um vector store).
    private static readonly (string[] Keywords, string Chunk)[] KnowledgeBase =
    [
        (["item", "itens"],
            "IDs de item seguem o padrão ITEM-XXXXXXXX. Cada item tem nome, status e quantidade."),
        (["status", "estado"],
            "Status possíveis de um item: active, pending, archived, locked. " +
            "Itens em status 'locked' não podem ser alterados pela tool update_item."),
        (["calcular", "cálculo", "calculo", "soma", "conta"],
            "Operações aritméticas devem usar a tool 'calculate' (add, sub, mul, div) — nunca calcule de cabeça."),
        (["hora", "horário", "horario", "tempo", "data"],
            "Datas e horários devem vir da tool 'get_time', que retorna UTC com deslocamento opcional."),
    ];

    public RagPromptEnricher(ILogger<RagPromptEnricher> logger) => _logger = logger;

    public Task<EnrichmentResult> EnrichAsync(
        string            input,
        string            tenantId,
        CancellationToken ct = default)
    {
        var lowered = input.ToLowerInvariant();

        var chunks = KnowledgeBase
            .Where(kb => kb.Keywords.Any(k => lowered.Contains(k)))
            .Select(kb => kb.Chunk)
            .Distinct()
            .ToList();

        if (chunks.Count == 0)
        {
            _logger.LogDebug("[RAG] Nenhum chunk relevante para tenant={Tenant}", tenantId);
            return Task.FromResult(new EnrichmentResult
            {
                EnrichedInput  = input,
                WasEnriched    = false,
                ChunksInjected = 0
            });
        }

        var query = ExtractQuery(input);

        var sb = new StringBuilder();
        sb.AppendLine("CONTEXTO RELEVANTE (base de conhecimento):");
        foreach (var chunk in chunks)
            sb.AppendLine($"- {chunk}");
        sb.AppendLine();
        sb.AppendLine("SOLICITAÇÃO DO USUÁRIO:");
        sb.Append(input);

        _logger.LogInformation("[RAG] Enriquecido tenant={Tenant} chunks={Count}", tenantId, chunks.Count);

        return Task.FromResult(new EnrichmentResult
        {
            EnrichedInput  = sb.ToString(),
            WasEnriched    = true,
            QueryUsed      = query,
            ChunksInjected = chunks.Count
        });
    }

    /// <summary>
    /// Deriva a query de recuperação: prefere um ID de entidade explícito no
    /// input e cai para as primeiras palavras quando não há nenhum.
    /// </summary>
    private static string ExtractQuery(string input)
    {
        var entityId = EntityIdRegex().Match(input);
        if (entityId.Success) return entityId.Value;

        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Take(8));
    }

    // PONTO DE EXTENSÃO — padrão de ID de entidade do domínio.
    [GeneratedRegex(@"ITEM-[A-Za-z0-9]+")]
    private static partial Regex EntityIdRegex();
}
