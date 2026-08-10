using System.Text.Json.Nodes;

namespace Domain.AI.LLM;

public abstract record LLMContent;
public sealed record LLMText(string Text) : LLMContent;
public sealed record LLMToolUse(string Id, string Name, JsonNode? Input) : LLMContent;
public sealed record LLMToolResult(string ToolUseId, string Content, bool IsError = false) : LLMContent;

public sealed record LLMMessage
{
    public required string                   Role    { get; init; }
    public required IReadOnlyList<LLMContent> Content { get; init; }

    public static LLMMessage User(string text) => new()
    {
        Role    = "user",
        Content = [new LLMText(text)]
    };

    public static LLMMessage Assistant(IReadOnlyList<LLMContent> content) => new()
    {
        Role    = "assistant",
        Content = content
    };

    public static LLMMessage ToolResults(IEnumerable<LLMToolResult> results) => new()
    {
        Role    = "user",
        Content = results.Cast<LLMContent>().ToList()
    };
}

public sealed record LLMToolDefinition(string Name, string Description, JsonNode InputSchema);

public sealed record LLMRequest
{
    public required string                           Model     { get; init; }
    public required int                              MaxTokens { get; init; }
    public          string?                          System    { get; init; }
    public required IReadOnlyList<LLMMessage>        Messages  { get; init; }
    public          IReadOnlyList<LLMToolDefinition>? Tools    { get; init; }
}

public sealed record LLMResponse
{
    public required string                    StopReason { get; init; }
    public required IReadOnlyList<LLMContent> Content    { get; init; }

    /// <summary>Modelo que efetivamente respondeu; null quando o provider não informa.</summary>
    public string? Model { get; init; }

    /// <summary>Tokens consumidos/produzidos nesta chamada. 0 = desconhecido.</summary>
    public int InputTokens  { get; init; }
    public int OutputTokens { get; init; }

    public string?                 Text     => Content.OfType<LLMText>().FirstOrDefault()?.Text;
    public IEnumerable<LLMToolUse> ToolUses => Content.OfType<LLMToolUse>();
}

public interface ILLMClient
{
    Task<LLMResponse> CompleteAsync(LLMRequest request, CancellationToken ct = default);
}
