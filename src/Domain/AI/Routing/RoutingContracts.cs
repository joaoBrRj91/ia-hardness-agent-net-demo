using System.Text.Json.Serialization;
using Domain.AI.Agents;
using Domain.AI.Tools;

namespace Domain.AI.Routing;

public sealed record RouteDecision
{
    [JsonPropertyName("intent")]
    public required string Intent { get; init; }

    [JsonPropertyName("confidence")]
    public required double Confidence { get; init; }

    [JsonPropertyName("reasoning")]
    public required string Reasoning { get; init; }

    /// <summary>
    /// Parâmetros extraídos do input pelo Router.
    /// Ex: { "transaction_id": "TXN-ABC123" }
    /// Evita re-parsear o input em cada agente downstream.
    /// </summary>
    [JsonPropertyName("params")]
    public Dictionary<string, string> Params { get; init; } = [];
}

public sealed record RoutedResponse
{
    public required string      Intent      { get; init; }
    public required double      Confidence  { get; init; }
    public required string      Output      { get; init; }
    public required string      HandlerName { get; init; }
    public required bool        WasFallback { get; init; }
    public          AgentState? AgentTrace  { get; init; }
}

/// <summary>
/// Contrato uniforme para todos os handlers do registry.
/// SemanticRouter não conhece os tipos concretos — apenas este contrato.
/// </summary>
public interface IAgentHandler
{
    string HandlerName         { get; }
    string TargetIntent        { get; }
    double ConfidenceThreshold { get; }

    Task<RoutedResponse> HandleAsync(
        string               input,
        RouteDecision        decision,
        ToolExecutionContext context,
        CancellationToken    ct = default);
}
