using HardnessAI.Tests.Support;
using Infrastructure.AI.Observability;
using Microsoft.Extensions.Logging;

namespace HardnessAI.Tests.Observability;

public class HarnessLogScopeTests
{
    [Fact]
    public void Uses_GenAiConventions_keys_and_omits_null_correlation()
    {
        using var provider = new CapturingLoggerProvider();
        var logger = provider.CreateLogger("Test");

        using (HarnessLogScope.Begin(logger, "corr-1", "tenant-a", "user-a"))
            logger.LogInformation("with correlation");

        using (HarnessLogScope.Begin(logger, null, "tenant-a", "user-a"))
            logger.LogInformation("without correlation");

        var with = Assert.Single(provider.Entries, e => e.Message == "with correlation");
        Assert.Equal("corr-1", with.Scope[GenAiConventions.CorrelationId]);
        Assert.Equal("tenant-a", with.Scope[GenAiConventions.TenantId]);
        Assert.Equal("user-a", with.Scope[GenAiConventions.UserId]);

        var without = Assert.Single(provider.Entries, e => e.Message == "without correlation");
        Assert.False(without.Scope.ContainsKey(GenAiConventions.CorrelationId));
        Assert.Equal("tenant-a", without.Scope[GenAiConventions.TenantId]);
        Assert.Equal("user-a", without.Scope[GenAiConventions.UserId]);
    }
}
