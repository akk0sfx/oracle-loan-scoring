using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ScoringApi.IntegrationTests;

/// <summary>
/// Real ODP.NET failure (no fake): a connection string to a port where nothing listens must
/// produce 503 DATABASE_UNAVAILABLE, and /health must report Unhealthy. Needs no container.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DatabaseUnavailableTests
{
    private const string ClosedPortConnectionString =
        "User Id=SCORING;Password=x;Data Source=//127.0.0.1:1/FREEPDB1;Connection Timeout=3";

    [Fact]
    public async Task Evaluate_WhenDatabaseUnreachable_Returns503()
    {
        await using var factory = new OracleApiFactory(ClosedPortConnectionString);
        using var client = factory.CreateAuthorizedClient();

        using var response = await client.PostAsJsonAsync("/api/v1/scoring/evaluate", new
        {
            applicationId = Guid.NewGuid().ToString(), amount = 500000m, termMonths = 24, monthlyIncome = 120000m, purposeCode = "CONSUMER",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("DATABASE_UNAVAILABLE");
    }

    [Fact]
    public async Task Health_WhenDatabaseUnreachable_Returns503Unhealthy()
    {
        await using var factory = new OracleApiFactory(ClosedPortConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).ShouldBe("{\"status\":\"Unhealthy\"}");
    }
}
