using System.Diagnostics;

namespace Infrastructure.AI.Observability;

/// <summary>
/// Spans internos do loop agentic (iteração / model call / tool execution).
/// Intrusivo por natureza, mas atrás de interface — NoOpAgentDiagnostics
/// mantém agentes funcionais sem telemetria.
/// </summary>
public interface IAgentDiagnostics
{
    Activity? StartIteration(int index);
    Activity? StartModelCall(string model, int maxTokens);
    Activity? StartToolExecution(string toolName);
    void RecordAuthzDenial(string toolName, string reason);
    void RecordToolResult(string toolName, bool success);
}
