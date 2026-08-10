using System.Collections.Concurrent;

namespace Infrastructure.AI.Observability;

/// <summary>
/// Acumulador de tokens por request (lifetime Scoped). Cada call site de
/// ILLMClient registra o usage logo após CompleteAsync; o InstrumentedAIHarness
/// lê os totais para tags do span raiz e cálculo de custo.
///
/// Premissa: um ProcessAsync por scope de DI (1 HTTP request = 1 scope = 1 harness
/// call). Uma segunda chamada no mesmo scope somaria os totais.
/// </summary>
public sealed class TokenUsageAccumulator
{
    private readonly ConcurrentDictionary<string, (long In, long Out)> _byModel = new();

    public void Record(string model, long inputTokens, long outputTokens)
        => _byModel.AddOrUpdate(
            model,
            (inputTokens, outputTokens),
            (_, cur) => (cur.In + inputTokens, cur.Out + outputTokens));

    public long TotalInput  => _byModel.Values.Sum(v => v.In);
    public long TotalOutput => _byModel.Values.Sum(v => v.Out);

    /// <summary>Modelo com maior volume de tokens — proxy do modelo "principal" da operação.</summary>
    public string? PrimaryModel => _byModel.Count == 0
        ? null
        : _byModel.MaxBy(kv => kv.Value.In + kv.Value.Out).Key;

    public IReadOnlyDictionary<string, (long In, long Out)> ByModel => _byModel;
}
