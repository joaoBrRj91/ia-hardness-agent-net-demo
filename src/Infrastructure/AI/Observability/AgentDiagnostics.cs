using System.Diagnostics;

namespace Infrastructure.AI.Observability;

public sealed class AgentDiagnostics(AIHarnessMetrics metrics) : IAgentDiagnostics
{
    // Internal: trabalho local, sem rede.
    public Activity? StartIteration(int index)
    {
        var a = AIDiagnostics.Source.StartActivity("agent.iteration", ActivityKind.Internal);
        a?.SetTag(GenAiConventions.IterationIndex, index);
        return a;
    }

    // Client + convenção OTel: nome do span é "{operation} {model}".
    public Activity? StartModelCall(string model, int maxTokens)
    {
        var a = AIDiagnostics.Source.StartActivity($"chat {model}", ActivityKind.Client);
        a?.SetTag(GenAiConventions.System, "anthropic");
        a?.SetTag(GenAiConventions.OperationName, "chat");
        a?.SetTag(GenAiConventions.RequestModel, model);
        a?.SetTag(GenAiConventions.RequestMaxTokens, maxTokens);
        return a;
    }

    public Activity? StartToolExecution(string toolName)
    {
        var a = AIDiagnostics.Source.StartActivity($"execute_tool {toolName}", ActivityKind.Internal);
        a?.SetTag(GenAiConventions.ToolName, toolName);
        return a;
    }

    public void RecordAuthzDenial(string toolName, string reason)
    {
        // Activity.Current aqui é o span da tool — graças ao AsyncLocal.
        // String, não bool: o tail_sampling avalia policies contra QUALQUER span
        // do trace, então marcar o span filho já retém o trace inteiro.
        Activity.Current?.SetTag(GenAiConventions.AuthzDenied, "true");
        Activity.Current?.AddEvent(new ActivityEvent("authz.denied",
            tags: new ActivityTagsCollection
            {
                { GenAiConventions.ToolName, toolName },
                { "denial_reason", reason }
            }));

        metrics.RecordAuthzDenial(toolName);
    }

    public void RecordToolResult(string toolName, bool success)
        => metrics.RecordToolInvocation(toolName, success);
}
