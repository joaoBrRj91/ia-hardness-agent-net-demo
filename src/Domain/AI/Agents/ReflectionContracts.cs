using System.Text.Json.Serialization;

namespace Domain.AI.Agents;

/// <summary>
/// Contrato tipado entre Generator e Critic.
/// Serializado como Structured Output — o Critic DEVE retornar este schema.
/// </summary>
public sealed record CriticFeedback
{
    /// <summary>0.0 (inaceitável) → 1.0 (perfeito).</summary>
    [JsonPropertyName("score")]
    public required double Score { get; init; }

    /// <summary>Score >= threshold do domínio — pré-computado pelo Critic.</summary>
    [JsonPropertyName("is_acceptable")]
    public required bool IsAcceptable { get; init; }

    /// <summary>Problemas concretos identificados no Draft.</summary>
    [JsonPropertyName("issues")]
    public required List<string> Issues { get; init; }

    /// <summary>Sugestões específicas de melhoria para o Generator.</summary>
    [JsonPropertyName("suggestions")]
    public required List<string> Suggestions { get; init; }

    /// <summary>Raciocínio do Critic — auditável.</summary>
    [JsonPropertyName("reasoning")]
    public required string Reasoning { get; init; }
}

public sealed record ReflectionState
{
    public required string               Goal            { get; init; }
    public required string               TenantId        { get; init; }
    public          string?              CurrentDraft    { get; init; }
    public          List<CriticFeedback> FeedbackHistory { get; init; } = [];
    public          int                  RefinementCount { get; init; } = 0;
    public          bool                 IsAccepted      { get; init; } = false;
    public          string?              FinalOutput     { get; init; }

    public ReflectionState WithDraft(string draft)
        => this with { CurrentDraft = draft };

    public ReflectionState WithFeedback(CriticFeedback feedback) => this with
    {
        FeedbackHistory = [..FeedbackHistory, feedback],
        RefinementCount = RefinementCount + 1,
        IsAccepted      = feedback.IsAcceptable,
        FinalOutput     = feedback.IsAcceptable ? CurrentDraft : null
    };
}
