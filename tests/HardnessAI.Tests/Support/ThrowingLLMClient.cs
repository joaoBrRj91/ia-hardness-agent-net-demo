using Domain.AI.LLM;

namespace HardnessAI.Tests.Support;

/// <summary>ILLMClient que sempre lança a exception informada.</summary>
public sealed class ThrowingLLMClient(Exception exception) : ILLMClient
{
    public Task<LLMResponse> CompleteAsync(LLMRequest request, CancellationToken ct = default)
        => throw exception;
}
