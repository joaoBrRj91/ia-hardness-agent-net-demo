using Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HardnessAI.Tests.Support;

/// <summary>
/// Monta um ServiceProvider com AddAIHarness (LLM fake, tenant permitido) e o
/// provider de captura de logs, sem passar pela Api.
/// </summary>
public sealed class HarnessTestHost : IDisposable
{
    private readonly ServiceProvider _provider;

    public CapturingLoggerProvider Logs { get; } = new();

    public HarnessTestHost(Action<IServiceCollection>? configure = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LLM:UseFake"] = "true",
                ["AllowedTenants:0"] = "tenant-demo"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Debug).AddProvider(Logs));
        services.AddAIHarness(config);
        configure?.Invoke(services);

        _provider = services.BuildServiceProvider(validateScopes: true);
    }

    public IServiceScope CreateScope() => _provider.CreateScope();

    public void Dispose() => _provider.Dispose();
}
