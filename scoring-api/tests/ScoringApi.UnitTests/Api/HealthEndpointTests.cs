using System.Net;
using System.Text.Json;

namespace ScoringApi.UnitTests.Api;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Health_ReturnsHealthyJson_WithoutApiKey()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("status").GetString().ShouldBe("Healthy");
    }
}
