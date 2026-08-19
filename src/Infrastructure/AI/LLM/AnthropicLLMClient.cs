using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Domain.AI.LLM;
using System.Text.Json.Nodes;

namespace Infrastructure.AI.LLM;

internal sealed class AnthropicLLMClient : ILLMClient
{
    private readonly AnthropicClient _inner;

    public AnthropicLLMClient(AnthropicClient inner) => _inner = inner;

    public async Task<LLMResponse> CompleteAsync(LLMRequest request, CancellationToken ct = default)
    {
        var parameters = new MessageParameters
        {
            Model     = request.Model,
            MaxTokens = request.MaxTokens,
            System    = request.System is not null ? [new SystemMessage(request.System)] : null,
            Messages  = request.Messages.Select(ToSdkMessage).ToList(),
            Tools     = request.Tools?.Select(t =>
                new Anthropic.SDK.Common.Tool(new Anthropic.SDK.Common.Function(t.Name, t.Description, t.InputSchema))).ToList()
        };

        var response = await _inner.Messages.GetClaudeMessageAsync(parameters, ct);

        return new LLMResponse
        {
            StopReason   = response.StopReason ?? "end_turn",
            Content      = response.Content.Select(ToDomainContent).OfType<LLMContent>().ToList(),
            Model        = response.Model ?? request.Model,
            InputTokens  = response.Usage?.InputTokens  ?? 0,
            OutputTokens = response.Usage?.OutputTokens ?? 0
        };
    }

    private static Message ToSdkMessage(LLMMessage msg)
    {
        var role    = msg.Role == "assistant" ? RoleType.Assistant : RoleType.User;
        var content = msg.Content.Select(ToSdkContent).OfType<ContentBase>().ToList();
        return new Message { Role = role, Content = content };
    }

    private static ContentBase? ToSdkContent(LLMContent c) => c switch
    {
        LLMText(var text)                            => new TextContent { Text = text },
        LLMToolUse(var id, var name, var input)      => new ToolUseContent
                                                        {
                                                            Id    = id,
                                                            Name  = name,
                                                            Input = input as JsonObject
                                                        },
        LLMToolResult(var uid, var content, _)       => new ToolResultContent
                                                        {
                                                            ToolUseId = uid,
                                                            Content   = [new TextContent { Text = content }]
                                                        },
        _ => null
    };

    private static LLMContent? ToDomainContent(ContentBase b) => b switch
    {
        TextContent    tc => new LLMText(tc.Text ?? string.Empty),
        ToolUseContent tu => new LLMToolUse(tu.Id, tu.Name, tu.Input),
        _                 => null
    };
}
