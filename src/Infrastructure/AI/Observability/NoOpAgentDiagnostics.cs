using System.Diagnostics;

namespace Infrastructure.AI.Observability;

/// <summary>No-op para testes: evita ActivitySource global vazando entre testes paralelos.</summary>
public sealed class NoOpAgentDiagnostics : IAgentDiagnostics
{
    public Activity? StartIteration(int index) => null;
    public Activity? StartModelCall(string model, int maxTokens) => null;
    public Activity? StartToolExecution(string toolName) => null;
    public void RecordAuthzDenial(string toolName, string reason) { }
    public void RecordToolResult(string toolName, bool success) { }
}
