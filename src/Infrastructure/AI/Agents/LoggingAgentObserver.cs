using Domain.AI.Agents;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Agents;

public sealed class LoggingAgentObserver : IAgentObserver
{
    private readonly ILogger<LoggingAgentObserver> _logger;

    public LoggingAgentObserver(ILogger<LoggingAgentObserver> logger)
        => _logger = logger;

    public Task OnStepAsync(AgentState state, AgentStep step, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[ReAct][{StepType}] iter={Iter} tenant={Tenant} | {Content}",
            step.StepType,
            state.IterationCount,
            state.TenantId,
            step.Content.Length > 200 ? step.Content[..200] + "…" : step.Content);

        return Task.CompletedTask;
    }

    public Task OnCompleteAsync(AgentState finalState, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[ReAct][Complete] iter={Iter} tenant={Tenant} steps={Steps} | answer_len={Len}",
            finalState.IterationCount,
            finalState.TenantId,
            finalState.Steps.Count,
            finalState.FinalAnswer?.Length ?? 0);

        return Task.CompletedTask;
    }
}
