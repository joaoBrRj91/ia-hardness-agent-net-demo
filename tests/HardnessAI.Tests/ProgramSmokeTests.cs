using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HardnessAI.Tests;

public class ProgramSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ProgramSmokeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
            b.UseSetting("LLM:UseFake", "true")
             .UseSetting("AllowedTenants:0", "tenant-demo"));
    }

    [Fact]
    public async Task Health_returns_200()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
