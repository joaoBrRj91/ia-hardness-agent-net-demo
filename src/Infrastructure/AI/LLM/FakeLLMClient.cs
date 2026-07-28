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

    public Task<LLMResponse> CompleteAsync(LLMRequest request, CancellationToken ct = default)
    {
        if (_queue.TryDequeue(out var queued))
            return Task.FromResult(queued);

        return Task.FromResult(BuildSmartDefault(request));
    }

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
