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

    // Base de conhecimento estática do domínio PSP.
    private static readonly (string[] Keywords, string Chunk)[] KnowledgeBase =
    [
        (["estorno", "refund", "reembolso"],
            "Política de estornos: irreversíveis após confirmação do PSP; exigem motivo auditável; " +
            "permitidos apenas para transações em status 'settled' ou 'authorized'."),
        (["chargeback", "disputa", "contestação"],
            "Chargebacks devem ser respondidos em até 7 dias corridos com evidências de entrega/autorização."),
        (["txn", "transação", "transacao", "transaction"],
            "IDs de transação seguem o padrão TXN-XXXXXXXX. Status possíveis: authorized, settled, refunded, declined."),
        (["fraude", "risco", "risk"],
            "Sinais de risco: velocidade anômala de transações, mismatch de BIN/país, valor muito acima do ticket médio."),
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
        sb.AppendLine("CONTEXTO RELEVANTE (base de conhecimento PSP):");
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

    private static string ExtractQuery(string input)
    {
        var txn = TxnRegex().Match(input);
        if (txn.Success) return txn.Value;

        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Take(8));
    }

    [GeneratedRegex(@"TXN-[A-Za-z0-9]+")]
    private static partial Regex TxnRegex();
}
