using System.Text.Json;
using System.Text.Json.Nodes;
using Domain.AI.Tools;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Tools;

/// <summary>
/// Módulo 3.1 — Tool definitions + handlers de DEMONSTRAÇÃO.
///
/// PONTO DE EXTENSÃO: este arquivo é o "Hello World" do harness. Existe apenas
/// para exercitar o pipeline end-to-end (ReAct + autorização + telemetria) sem
/// depender de nenhum sistema externo. Ao adaptar o template para um domínio
/// real, substitua este arquivo inteiro e ajuste DemoToolsRegistration.
///
/// Ao substituir, preserve estas duas propriedades — o resto da arquitetura
/// depende delas para continuar demonstrável:
///   1. pelo menos uma tool com IsMutating = false (caminho feliz);
///   2. pelo menos uma tool com IsMutating = true, para que a ReadOnlyPolicy e a
///      trilha de auditoria (app.ai.authz_denied) continuem sendo exercitadas.
/// </summary>
internal static class DemoFixtures
{
    /// <summary>
    /// "Banco de dados" em memória — determinístico por item id, sem I/O.
    /// O hash do id define status e quantidade, então a mesma pergunta sempre
    /// produz a mesma resposta (essencial para cenários offline reproduzíveis).
    /// </summary>
    public static Item Lookup(string itemId)
    {
        var seed   = StableHash(itemId);
        var status = (seed % 4) switch
        {
            0 => "active",
            1 => "pending",
            2 => "archived",
            _ => "locked"
        };

        return new Item(
            Id:        itemId,
            Name:      $"Item de demonstração #{seed % 1000:D3}",
            Status:    status,
            Quantity:  seed % 250,
            UpdatedAt: DemoClock.Now.AddMinutes(-(seed % 4320)));
    }

    /// <summary>
    /// FNV-1a 32 bits. String.GetHashCode() é randomizado por processo no .NET,
    /// então não serve para fixtures: o mesmo ID daria status diferente a cada
    /// execução e os cenários offline deixariam de ser reproduzíveis.
    /// </summary>
    private static int StableHash(string value)
    {
        const uint offsetBasis = 2166136261;
        const uint prime       = 16777619;

        var hash = offsetBasis;
        foreach (var c in value)
        {
            hash ^= c;
            hash *= prime;
        }

        return (int)(hash & 0x7FFFFFFF);
    }

    public sealed record Item(
        string   Id,
        string   Name,
        string   Status,
        int      Quantity,
        DateTime UpdatedAt);
}

/// <summary>
/// Relógio fixo. Um template precisa ser reproduzível: com DateTime.UtcNow os
/// cenários offline e os traces mudariam a cada execução.
/// </summary>
internal static class DemoClock
{
    public static DateTime Now { get; } = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
}

// ── echo ──────────────────────────────────────────────────────────────────

public sealed class EchoDefinition : IToolDefinition
{
    public string   Name        => "echo";
    public string   Description => "Devolve exatamente o texto recebido. Útil para confirmar que o dispatch de tools está funcionando.";
    public bool     IsMutating  => false;

    public JsonNode InputSchema => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "text": {
              "type": "string",
              "description": "Texto a ser devolvido sem alteração."
            }
          },
          "required": ["text"]
        }
        """)!;
}

public sealed class EchoHandler : IToolHandler
{
    private readonly ILogger<EchoHandler> _logger;

    public EchoHandler(ILogger<EchoHandler> logger) => _logger = logger;

    public string ToolName => "echo";

    public Task<string> HandleAsync(JsonNode? input, CancellationToken ct = default)
    {
        var text = input?["text"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult(DemoToolErrors.InvalidInput("text é obrigatório."));

        _logger.LogInformation("[Tool] echo len={Length}", text.Length);

        return Task.FromResult(JsonSerializer.Serialize(new { echoed = text }));
    }
}

internal static class DemoToolErrors
{
    public static string InvalidInput(string message) =>
        JsonSerializer.Serialize(new { error = "invalid_input", message });
}

// ── get_time ──────────────────────────────────────────────────────────────

public sealed class GetTimeDefinition : IToolDefinition
{
    public string   Name        => "get_time";
    public string   Description => "Retorna a data e hora atuais do sistema de demonstração em UTC, com deslocamento opcional.";
    public bool     IsMutating  => false;

    public JsonNode InputSchema => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "offset_hours": {
              "type": "integer",
              "description": "Deslocamento em horas a aplicar sobre o horário UTC. Padrão: 0."
            }
          },
          "required": []
        }
        """)!;
}

public sealed class GetTimeHandler : IToolHandler
{
    private readonly ILogger<GetTimeHandler> _logger;

    public GetTimeHandler(ILogger<GetTimeHandler> logger) => _logger = logger;

    public string ToolName => "get_time";

    public Task<string> HandleAsync(JsonNode? input, CancellationToken ct = default)
    {
        var offset = input?["offset_hours"]?.GetValue<int>() ?? 0;

        if (offset is < -12 or > 14)
            return Task.FromResult(DemoToolErrors.InvalidInput("offset_hours deve estar entre -12 e 14."));

        var utc = DemoClock.Now;
        _logger.LogInformation("[Tool] get_time offset={Offset}", offset);

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            utc          = utc.ToString("O"),
            offset_hours = offset,
            local        = utc.AddHours(offset).ToString("O")
        }));
    }
}

// ── calculate ─────────────────────────────────────────────────────────────

public sealed class CalculateDefinition : IToolDefinition
{
    public string   Name        => "calculate";
    public string   Description => "Executa uma operação aritmética simples entre dois números inteiros (add, sub, mul, div).";
    public bool     IsMutating  => false;

    public JsonNode InputSchema => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "a":  { "type": "integer", "description": "Primeiro operando." },
            "b":  { "type": "integer", "description": "Segundo operando." },
            "op": {
              "type": "string",
              "enum": ["add", "sub", "mul", "div"],
              "description": "Operação a executar."
            }
          },
          "required": ["a", "b", "op"]
        }
        """)!;
}

public sealed class CalculateHandler : IToolHandler
{
    private readonly ILogger<CalculateHandler> _logger;

    public CalculateHandler(ILogger<CalculateHandler> logger) => _logger = logger;

    public string ToolName => "calculate";

    public Task<string> HandleAsync(JsonNode? input, CancellationToken ct = default)
    {
        var a  = input?["a"]?.GetValue<int>();
        var b  = input?["b"]?.GetValue<int>();
        var op = input?["op"]?.GetValue<string>();

        if (a is null || b is null || string.IsNullOrWhiteSpace(op))
            return Task.FromResult(DemoToolErrors.InvalidInput("a, b e op são obrigatórios."));

        if (op == "div" && b == 0)
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                error   = "division_by_zero",
                message = "Divisão por zero não é permitida."
            }));

        decimal? result = op switch
        {
            "add" => a.Value + b.Value,
            "sub" => a.Value - b.Value,
            "mul" => (decimal)a.Value * b.Value,
            "div" => (decimal)a.Value / b.Value,
            _     => null
        };

        if (result is null)
            return Task.FromResult(DemoToolErrors.InvalidInput($"Operação '{op}' desconhecida. Use add, sub, mul ou div."));

        _logger.LogInformation("[Tool] calculate {A} {Op} {B}", a, op, b);

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            a = a.Value,
            b = b.Value,
            op,
            result = result.Value
        }));
    }
}

// ── lookup_item ───────────────────────────────────────────────────────────

public sealed class LookupItemDefinition : IToolDefinition
{
    public string   Name        => "lookup_item";
    public string   Description => "Consulta o estado atual de um item pelo seu ID (padrão ITEM-XXXXXXXX).";
    public bool     IsMutating  => false;

    public JsonNode InputSchema => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "item_id": {
              "type": "string",
              "description": "Identificador do item, ex: ITEM-ABC12345"
            }
          },
          "required": ["item_id"]
        }
        """)!;
}

public sealed class LookupItemHandler : IToolHandler
{
    private readonly ILogger<LookupItemHandler> _logger;

    public LookupItemHandler(ILogger<LookupItemHandler> logger) => _logger = logger;

    public string ToolName => "lookup_item";

    public Task<string> HandleAsync(JsonNode? input, CancellationToken ct = default)
    {
        var itemId = input?["item_id"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(itemId))
            return Task.FromResult(DemoToolErrors.InvalidInput("item_id é obrigatório."));

        var item = DemoFixtures.Lookup(itemId);
        _logger.LogInformation("[Tool] lookup_item item={Item} status={Status}", itemId, item.Status);

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            item_id    = item.Id,
            name       = item.Name,
            status     = item.Status,
            quantity   = item.Quantity,
            updated_at = item.UpdatedAt.ToString("O"),
            updatable  = item.Status is not "locked"
        }));
    }
}

// ── update_item (MUTATING) ────────────────────────────────────────────────

public sealed class UpdateItemDefinition : IToolDefinition
{
    public string   Name        => "update_item";
    public string   Description => "Altera a quantidade de um item. AÇÃO QUE MUTA ESTADO — exige justificativa auditável.";
    public bool     IsMutating  => true;

    public JsonNode InputSchema => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "item_id": {
              "type": "string",
              "description": "Identificador do item a alterar."
            },
            "quantity": {
              "type": "integer",
              "description": "Nova quantidade do item."
            },
            "reason": {
              "type": "string",
              "description": "Motivo da alteração para trilha de auditoria."
            }
          },
          "required": ["item_id", "quantity", "reason"]
        }
        """)!;
}

public sealed class UpdateItemHandler : IToolHandler
{
    private readonly ILogger<UpdateItemHandler> _logger;

    public UpdateItemHandler(ILogger<UpdateItemHandler> logger) => _logger = logger;

    public string ToolName => "update_item";

    public Task<string> HandleAsync(JsonNode? input, CancellationToken ct = default)
    {
        var itemId   = input?["item_id"]?.GetValue<string>();
        var quantity = input?["quantity"]?.GetValue<int>();
        var reason   = input?["reason"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(itemId) || quantity is null || string.IsNullOrWhiteSpace(reason))
            return Task.FromResult(DemoToolErrors.InvalidInput("item_id, quantity e reason são obrigatórios."));

        var item = DemoFixtures.Lookup(itemId);

        // Caminho de erro deliberado: dá ao ReAct algo para observar e replanejar.
        if (item.Status is "locked")
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                error   = "item_locked",
                item_id = item.Id,
                status  = item.Status,
                message = $"Item em status '{item.Status}' não pode ser alterado."
            }));

        var changeId = $"CHG-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        _logger.LogWarning("[Tool] update_item item={Item} change={Change} reason={Reason}", itemId, changeId, reason);

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            change_id         = changeId,
            item_id           = item.Id,
            previous_quantity = item.Quantity,
            new_quantity      = quantity.Value,
            status            = "update_applied",
            reason
        }));
    }
}
