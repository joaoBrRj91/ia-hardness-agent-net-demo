namespace Infrastructure.AI.Observability;

public sealed class TelemetryOptions
{
    /// <summary>NUNCA true em produção sem revisão de compliance.</summary>
    public bool   RecordContent    { get; init; } = false;
    public int    MaxContentLength { get; init; } = 2_000;
    public string DefaultModel     { get; init; } = "claude-sonnet-4-6";

    /// <summary>Limiar de custo acima do qual o trace é marcado como anômalo.</summary>
    public double CostAlertThresholdUsd { get; init; } = 0.50;

    /// <summary>Preço por 1M tokens — config-driven, muda sem redeploy.</summary>
    public Dictionary<string, ModelPricing> Pricing { get; init; } = new();
}

public sealed record ModelPricing(decimal InputPerMillion, decimal OutputPerMillion);
