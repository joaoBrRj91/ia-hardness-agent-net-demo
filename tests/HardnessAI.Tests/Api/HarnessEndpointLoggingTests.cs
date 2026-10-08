using System.Net;
using System.Net.Http.Json;
using Domain.AI.LLM;
using HardnessAI.Tests.Support;
using Infrastructure.AI.Observability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace HardnessAI.Tests.Api;

public class HarnessEndpointLoggingTests
{
    private static WebApplicationFactory<Program> CreateFactory(
        Exception failure, CapturingLoggerProvider logs) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("LLM:UseFake", "true");
            b.UseSetting("AllowedTenants:0", "tenant-demo");
            b.ConfigureLogging(l => l.AddProvider(logs));
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<ILLMClient>();
                s.AddSingleton<ILLMClient>(new ThrowingLLMClient(failure));
            });
        });

    private static CapturedLog ProgramError(CapturingLoggerProvider logs) =>
        Assert.Single(logs.Entries, l => l.Category == "Program" && l.Level == LogLevel.Error);

    [Fact]
    public async Task Llm_http_failure_returns_502_and_logs_error()
    {
        var logs = new CapturingLoggerProvider();
        var failure = new HttpRequestException("provider down");
        await using var factory = CreateFactory(failure, logs);

        var response = await factory.CreateClient().PostAsJsonAsync("/harness", new
        {
            input = "olá",
            tenantId = "tenant-demo",
            userId = "user-42",
            correlationId = "corr-http",
            skipEnrichment = true
        });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var error = ProgramError(logs);
        Assert.Same(failure, error.Exception);
        Assert.StartsWith("[Api] Falha no provedor LLM", error.Message);
        Assert.Equal("tenant-demo", error.Scope[GenAiConventions.TenantId]);
        Assert.Equal("user-42", error.Scope[GenAiConventions.UserId]);
        Assert.Equal("corr-http", error.Scope[GenAiConventions.CorrelationId]);
    }

    [Fact]
    public async Task Invalid_operation_returns_500_and_logs_error()
    {
        var logs = new CapturingLoggerProvider();
        var failure = new InvalidOperationException("config ausente");
        await using var factory = CreateFactory(failure, logs);

        var response = await factory.CreateClient().PostAsJsonAsync("/harness", new
        {
            input = "olá",
            tenantId = "tenant-demo",
            userId = "user-42",
            correlationId = "corr-500",
            skipEnrichment = true
        });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var error = ProgramError(logs);
        Assert.Same(failure, error.Exception);
        Assert.StartsWith("[Api] Configuração inválida", error.Message);
        Assert.Equal("corr-500", error.Scope[GenAiConventions.CorrelationId]);
    }

    [Fact]
    public async Task Omits_correlation_key_when_not_provided()
    {
        var logs = new CapturingLoggerProvider();
        await using var factory = CreateFactory(new HttpRequestException("down"), logs);

        var response = await factory.CreateClient().PostAsJsonAsync("/harness", new
        {
            input = "olá",
            tenantId = "tenant-demo",
            userId = "user-42",
            skipEnrichment = true
        });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var error = ProgramError(logs);
        Assert.False(error.Scope.ContainsKey(GenAiConventions.CorrelationId));
        Assert.Equal("tenant-demo", error.Scope[GenAiConventions.TenantId]);
        Assert.Equal("user-42", error.Scope[GenAiConventions.UserId]);
    }
}
