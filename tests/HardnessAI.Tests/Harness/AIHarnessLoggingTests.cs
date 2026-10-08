using Domain.AI.Harness;
using Domain.AI.LLM;
using Domain.AI.Tools;
using HardnessAI.Tests.Support;
using Infrastructure.AI.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HardnessAI.Tests.Harness;

public class AIHarnessLoggingTests
{
    private static readonly string[] CoveredCategories =
        ["AIHarness", "SemanticRouter", "ReActAgent", "LoggingAgentObserver"];

    private static HarnessRequest Request(string? correlationId) => new()
    {
        Input          = "Investigue o item ITEM-00000003",
        Context        = ToolExecutionContext.For("tenant-demo", "user-42", [], false),
        ForceIntent    = "investigate",
        CorrelationId  = correlationId,
        SkipEnrichment = true
    };

    private static bool IsCovered(CapturedLog log) =>
        CoveredCategories.Any(c => log.Category.EndsWith("." + c, StringComparison.Ordinal));

    [Fact]
    public async Task All_logs_carry_generated_correlation_id_in_scope()
    {
        using var host = new HarnessTestHost(s => s.AddSingleton<ILLMClient>(_ =>
        {
            var fake = new Infrastructure.AI.LLM.FakeLLMClient();
            fake.LoadScenario("react-tool-call");
            return fake;
        }));
        using var scope = host.CreateScope();
        var harness = scope.ServiceProvider.GetRequiredService<IAIHarness>();

        var response = await harness.ProcessAsync(Request(correlationId: null));

        var logs = host.Logs.Entries.Where(IsCovered).ToList();
        Assert.False(string.IsNullOrEmpty(response.CorrelationId));
        foreach (var category in CoveredCategories.Where(c => c != "ReActAgent"))
            Assert.Contains(logs, l => l.Category.EndsWith("." + category));

        Assert.All(logs, l =>
        {
            Assert.Equal(response.CorrelationId, l.Scope[GenAiConventions.CorrelationId]);
            Assert.Equal("tenant-demo", l.Scope[GenAiConventions.TenantId]);
            Assert.Equal("user-42",     l.Scope[GenAiConventions.UserId]);
        });
    }

    [Fact]
    public async Task Preserves_provided_correlation_id()
    {
        using var host = new HarnessTestHost();
        using var scope = host.CreateScope();
        var harness = scope.ServiceProvider.GetRequiredService<IAIHarness>();

        var response = await harness.ProcessAsync(Request("upstream-corr-1"));

        Assert.Equal("upstream-corr-1", response.CorrelationId);
        var logs = host.Logs.Entries.Where(IsCovered).ToList();
        Assert.NotEmpty(logs);
        Assert.All(logs, l =>
            Assert.Equal("upstream-corr-1", l.Scope[GenAiConventions.CorrelationId]));
    }

    [Fact]
    public async Task Error_log_is_inside_scope()
    {
        using var host = new HarnessTestHost(s =>
            s.AddSingleton<ILLMClient>(new ThrowingLLMClient(new HttpRequestException("boom"))));
        using var scope = host.CreateScope();
        var harness = scope.ServiceProvider.GetRequiredService<IAIHarness>();

        await Assert.ThrowsAsync<HttpRequestException>(
            () => harness.ProcessAsync(Request("corr-err")));

        var error = Assert.Single(host.Logs.Entries, l =>
            l.Level == LogLevel.Error && l.Message.StartsWith("[Harness] ERROR"));
        Assert.Equal("corr-err",    error.Scope[GenAiConventions.CorrelationId]);
        Assert.Equal("tenant-demo", error.Scope[GenAiConventions.TenantId]);
        Assert.Equal("user-42",     error.Scope[GenAiConventions.UserId]);
    }
}
