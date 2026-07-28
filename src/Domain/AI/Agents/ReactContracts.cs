namespace Domain.AI.Agents;

public enum AgentStepType
{
    Thought,
    Action,
    Observation,
    FinalAnswer
}

public sealed record AgentStep
{
    public required AgentStepType StepType      { get; init; }
    public required string        Content       { get; init; }
    public          string?       ToolName      { get; init; }
    public          string?       ToolInputJson { get; init; }
    public          DateTime      Timestamp     { get; init; } = DateTime.UtcNow;
}

public sealed record AgentState
{
    public required string          Goal           { get; init; }
    public required string          TenantId       { get; init; }
    public required string          UserId         { get; init; }
    public          List<AgentStep> Steps          { get; init; } = [];
    public          int             IterationCount { get; init; } = 0;
    public          bool            IsComplete     { get; init; } = false;
    public          string?         FinalAnswer    { get; init; }

    /// <summary>
    /// Imutável: retorna novo estado com step appended.
    /// Pattern: Event Sourcing light — cada mutação produz novo estado.
    /// </summary>
    public AgentState WithStep(AgentStep step) => this with
    {
        Steps          = [..Steps, step],
        IterationCount = step.StepType == AgentStepType.Action
            ? IterationCount + 1
            : IterationCount
    };

    public AgentState WithFinalAnswer(string answer) => this with
    {
        IsComplete  = true,
        FinalAnswer = answer,
        Steps       = [..Steps, new AgentStep
        {
            StepType = AgentStepType.FinalAnswer,
            Content  = answer
        }]
    };
}

/// <summary>
/// Hook de observabilidade — implementações:
///   LoggingAgentObserver      (Módulo 4)
///   OpenTelemetryObserver     (Módulo 6)
///   TestObserver              (testes unitários)
/// </summary>
public interface IAgentObserver
{
    Task OnStepAsync(AgentState state, AgentStep step, CancellationToken ct = default);
    Task OnCompleteAsync(AgentState finalState, CancellationToken ct = default);
}
