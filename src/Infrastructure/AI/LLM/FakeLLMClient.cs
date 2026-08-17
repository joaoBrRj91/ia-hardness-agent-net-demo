using System.Collections.Concurrent;
using Domain.AI.LLM;

namespace Infrastructure.AI.LLM;

public sealed class FakeLLMClient : ILLMClient
{
    private readonly ConcurrentQueue<LLMResponse> _queue = new();

    public void Enqueue(params LLMResponse[] responses)
    {
        foreach (var r in responses) _queue.Enqueue(r);
    }

    public void LoadScenario(string name) => Enqueue(FakeScenarios.Get(name));

    public Task<LLMResponse> CompleteAsync(LLMRequest request, CancellationToken ct = default)
    {
        var response = _queue.TryDequeue(out var queued)
            ? queued
            : BuildSmartDefault(request);

        return Task.FromResult(WithSyntheticUsage(response, request));
    }

    /// <summary>
    /// Usage sintético (~4 chars/token) para exercitar métricas de token/custo offline.
    /// </summary>
    private static LLMResponse WithSyntheticUsage(LLMResponse response, LLMRequest request)
    {
        if (response.InputTokens > 0 || response.OutputTokens > 0)
            return response;

        var inputChars = (request.System?.Length ?? 0) + request.Messages
            .SelectMany(m => m.Content)
            .Sum(EstimateChars);

        var outputChars = response.Content.Sum(EstimateChars);

        return response with
        {
            Model        = response.Model ?? request.Model,
            InputTokens  = Math.Max(1, inputChars  / 4),
            OutputTokens = Math.Max(1, outputChars / 4)
        };
    }

    private static int EstimateChars(LLMContent content) => content switch
    {
        LLMText(var text)              => text.Length,
        LLMToolUse(_, var name, var i) => name.Length + (i?.ToJsonString().Length ?? 0),
        LLMToolResult(_, var text, _)  => text.Length,
        _                              => 0
    };

    private static LLMResponse BuildSmartDefault(LLMRequest request)
    {
        var system = request.System ?? string.Empty;

        if (system.Contains("classificador de intenção", StringComparison.OrdinalIgnoreCase))
            return TextResponse("""{"intent":"summarize","confidence":0.85,"reasoning":"Fake router — default intent","params":{}}""");

        if (system.Contains("revisor especializado", StringComparison.OrdinalIgnoreCase))
            return TextResponse("""{"score":0.9,"is_acceptable":true,"issues":[],"suggestions":[],"reasoning":"Fake critic — always acceptable"}""");

        return TextResponse("Resposta simulada pelo FakeLLMClient (modo offline).");
    }

    private static LLMResponse TextResponse(string text) => new()
    {
        StopReason = "end_turn",
        Content    = [new LLMText(text)]
    };
}
