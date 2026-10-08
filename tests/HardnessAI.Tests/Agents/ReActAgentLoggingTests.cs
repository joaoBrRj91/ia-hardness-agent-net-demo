using System.Text.Json.Nodes;
using Domain.AI.LLM;
using Domain.AI.Tools;
using HardnessAI.Tests.Support;
using Infrastructure.AI.Agents;
using Infrastructure.AI.LLM;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HardnessAI.Tests.Agents;

public class ReActAgentLoggingTests
{
    private static readonly ToolExecutionContext Context =
        ToolExecutionContext.For("tenant-demo", "user-42", [], false);

    private static LLMResponse ToolUse(int n) => new()
    {
        StopReason = "tool_use",
        Content =
        [
            new LLMText($"Passo {n}"),
            new LLMToolUse($"toolu_{n}", "echo", JsonNode.Parse("""{"text":"x"}"""))
        ]
    };

    private static async Task<(AgentRun Run, IReadOnlyList<CapturedLog> Logs)> RunAsync(
        params LLMResponse[] responses)
    {
        using var host  = new HarnessTestHost();
        using var scope = host.CreateScope();
        scope.ServiceProvider.GetRequiredService<FakeLLMClient>().Enqueue(responses);
        var agent = scope.ServiceProvider.GetRequiredService<ReActAgent>();

        var state = await agent.RunAsync("investigue", Context);

        return (new AgentRun(state.FinalAnswer), host.Logs.Entries
            .Where(l => l.Category.EndsWith(".ReActAgent")).ToList());
    }

    private sealed record AgentRun(string? FinalAnswer);

    [Fact]
    public async Task Logs_warning_when_max_iterations_reached()
    {
        var (run, logs) = await RunAsync(Enumerable.Range(1, 8).Select(ToolUse).ToArray());

        var warning = Assert.Single(logs, l => l.Level == LogLevel.Warning);
        Assert.StartsWith("[ReAct] MaxIterations atingido", warning.Message);
        Assert.Equal(8, warning.Properties["MaxIterations"]);
        Assert.Contains("Agente não convergiu", run.FinalAnswer);
    }
}
