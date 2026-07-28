using System.Text.Json;
using System.Text.Json.Nodes;
using Domain.AI.Tools;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Tools;

/// <summary>
/// Módulo 3.1 — Tool definitions + handlers de exemplo para o domínio PSP.
/// As implementações são in-memory / determinísticas para permitir execução
/// end-to-end sem depender de um gateway real.
/// </summary>
internal static class PspFixtures
{
    // "Banco de dados" de transações em memória — determinístico por TXN id.
    public static Transaction Lookup(string transactionId)
    {
        var seed   = Math.Abs(transactionId.GetHashCode());
        var status = (seed % 4) switch
        {
            0 => "settled",
            1 => "authorized",
            2 => "refunded",
            _ => "declined"
        };
        var amount = 1000 + seed % 90000; // centavos
        return new Transaction(transactionId, status, amount / 100m, "BRL",
            DateTime.UtcNow.AddMinutes(-(seed % 4320)));
    }

    public sealed record Transaction(
        string   Id,
        string   Status,
        decimal  Amount,
        string   Currency,
        DateTime CreatedAt);
}

public sealed class CheckTransactionStatusDefinition : IToolDefinition
{
    public string   Name        => "check_transaction_status";
    public string   Description => "Consulta o status atual de uma transação de pagamento pelo seu ID (padrão TXN-XXXXXXXX).";
    public bool     IsMutating  => false;

    public JsonNode InputSchema => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "transaction_id": {
              "type": "string",
              "description": "Identificador da transação, ex: TXN-ABC12345"
            }
          },
          "required": ["transaction_id"]
        }
        """)!;
}

public sealed class CheckTransactionStatusHandler : IToolHandler
{
    private readonly ILogger<CheckTransactionStatusHandler> _logger;

    public CheckTransactionStatusHandler(ILogger<CheckTransactionStatusHandler> logger)
        => _logger = logger;

    public string ToolName => "check_transaction_status";

    public Task<string> HandleAsync(JsonNode? input, CancellationToken ct = default)
    {
        var txnId = input?["transaction_id"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(txnId))
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                error   = "invalid_input",
                message = "transaction_id é obrigatório."
            }));

        var txn = PspFixtures.Lookup(txnId);
        _logger.LogInformation("[Tool] check_transaction_status txn={Txn} status={Status}", txnId, txn.Status);

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            transaction_id = txn.Id,
            status         = txn.Status,
            amount         = txn.Amount,
            currency       = txn.Currency,
            created_at     = txn.CreatedAt.ToString("O"),
            refundable     = txn.Status is "settled" or "authorized"
        }));
    }
}

public sealed class IssueRefundDefinition : IToolDefinition
{
    public string   Name        => "issue_refund";
    public string   Description => "Emite o estorno (refund) de uma transação. AÇÃO IRREVERSÍVEL após confirmação do PSP.";
    public bool     IsMutating  => true;

    public JsonNode InputSchema => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "transaction_id": {
              "type": "string",
              "description": "Identificador da transação a estornar."
            },
            "reason": {
              "type": "string",
              "description": "Motivo do estorno para trilha de auditoria."
            }
          },
          "required": ["transaction_id", "reason"]
        }
        """)!;
}

public sealed class IssueRefundHandler : IToolHandler
{
    private readonly ILogger<IssueRefundHandler> _logger;

    public IssueRefundHandler(ILogger<IssueRefundHandler> logger) => _logger = logger;

    public string ToolName => "issue_refund";

    public Task<string> HandleAsync(JsonNode? input, CancellationToken ct = default)
    {
        var txnId  = input?["transaction_id"]?.GetValue<string>();
        var reason = input?["reason"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(txnId) || string.IsNullOrWhiteSpace(reason))
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                error   = "invalid_input",
                message = "transaction_id e reason são obrigatórios."
            }));

        var txn = PspFixtures.Lookup(txnId);

        if (txn.Status is not ("settled" or "authorized"))
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                error          = "not_refundable",
                transaction_id = txn.Id,
                status         = txn.Status,
                message        = $"Transação em status '{txn.Status}' não pode ser estornada."
            }));

        var refundId = $"RFND-{Guid.NewGuid():N}"[..13].ToUpperInvariant();
        _logger.LogWarning("[Tool] issue_refund txn={Txn} refund={Refund} reason={Reason}", txnId, refundId, reason);

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            refund_id      = refundId,
            transaction_id = txn.Id,
            amount         = txn.Amount,
            currency       = txn.Currency,
            status         = "refund_pending",
            reason
        }));
    }
}
