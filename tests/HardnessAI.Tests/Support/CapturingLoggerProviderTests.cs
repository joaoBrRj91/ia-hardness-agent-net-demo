using HardnessAI.Tests.Support;
using Microsoft.Extensions.Logging;

namespace HardnessAI.Tests.Support;

public class CapturingLoggerProviderTests
{
    [Fact]
    public void Records_active_scope_pairs()
    {
        using var provider = new CapturingLoggerProvider();
        var logger = provider.CreateLogger("Test.Category");

        using (logger.BeginScope(new Dictionary<string, object?> { ["app.correlation_id"] = "abc" }))
        {
            logger.LogWarning("hello {Name}", "world");
        }

        logger.LogInformation("outside");

        var inside = Assert.Single(provider.Entries, e => e.Level == LogLevel.Warning);
        Assert.Equal("Test.Category", inside.Category);
        Assert.Equal("hello world", inside.Message);
        Assert.Equal("abc", inside.Scope["app.correlation_id"]);
        Assert.Equal("world", inside.Properties["Name"]);

        var outside = Assert.Single(provider.Entries, e => e.Level == LogLevel.Information);
        Assert.Empty(outside.Scope);
    }
}
