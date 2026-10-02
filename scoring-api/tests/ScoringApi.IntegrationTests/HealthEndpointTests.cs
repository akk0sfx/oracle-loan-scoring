using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ScoringApi.IntegrationTests;

public sealed class HealthEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_ReturnsHealthyJson()
    {
        // Startup validation requires both settings; the Oracle probe is removed because
        // this test checks only the response format, not the database.
        using var client = factory
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Scoring:ApiKey", "test-key");
                builder.UseSetting("ConnectionStrings:Oracle", "Data Source=unused");
                builder.ConfigureTestServices(services =>
                    services.Configure<HealthCheckServiceOptions>(o => o.Registrations.Clear()));
            })
            .CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
    }
}
