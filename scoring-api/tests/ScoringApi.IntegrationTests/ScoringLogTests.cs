using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Oracle.ManagedDataAccess.Client;

namespace ScoringApi.IntegrationTests;

[Collection(OracleCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ScoringLogTests(OracleFixture oracle)
{
    [Fact]
    public async Task Evaluate_CommitsLogRow_AndHistoryReturnsItFirst()
    {
        await using var factory = new OracleApiFactory(oracle.ConnectionString);
        using var client = factory.CreateAuthorizedClient();
        var applicationId = Guid.NewGuid().ToString();

        await EvaluateAsync(client, applicationId, purposeCode: "CAR", income: 100000m);      // REVIEW, 680
        await EvaluateAsync(client, applicationId, purposeCode: "CONSUMER", income: 120000m); // APPROVE, 800

        // Read SCORING_LOG directly on a separate connection: rows are visible only if the API committed.
        var rows = await ReadLogAsync(applicationId);
        rows.Count.ShouldBe(2);
        var newest = rows.MaxBy(r => r.Id);
        newest.Score.ShouldBe(800);
        newest.Decision.ShouldBe("APPROVE");
        newest.Reasons.ShouldBe("DTI_LOW,PURPOSE_CONSUMER");

        using var response = await client.GetAsync($"/api/v1/scoring/{applicationId}/history");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.EnumerateArray().ToArray();
        items.Length.ShouldBe(2);
        items[0].GetProperty("id").GetInt64().ShouldBe(newest.Id);
        items[0].GetProperty("score").GetInt32().ShouldBe(800);
        items[1].GetProperty("score").GetInt32().ShouldBe(680);
        items[0].GetProperty("createdAt").GetString().ShouldEndWith("Z");
    }

    [Fact]
    public async Task History_UnknownApplication_ReturnsEmptyArray()
    {
        await using var factory = new OracleApiFactory(oracle.ConnectionString);
        using var client = factory.CreateAuthorizedClient();

        var body = await client.GetStringAsync($"/api/v1/scoring/{Guid.NewGuid()}/history");

        body.ShouldBe("[]");
    }

    [Fact]
    public async Task Health_WithRealDatabase_IsHealthy()
    {
        await using var factory = new OracleApiFactory(oracle.ConnectionString);
        using var client = factory.CreateClient();

        var body = await client.GetStringAsync("/health");

        body.ShouldBe("{\"status\":\"Healthy\"}");
    }

    private static async Task EvaluateAsync(HttpClient client, string applicationId, string purposeCode, decimal income)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/scoring/evaluate", new
        {
            applicationId, amount = 1000000m * (purposeCode == "CAR" ? 1 : 0.5m), termMonths = purposeCode == "CAR" ? 36 : 24,
            monthlyIncome = income, purposeCode,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<List<(long Id, int Score, string Decision, string Reasons)>> ReadLogAsync(string applicationId)
    {
        await using var connection = new OracleConnection(oracle.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.BindByName = true;
        command.CommandText = "SELECT ID, SCORE, DECISION, REASONS FROM SCORING_LOG WHERE APPLICATION_ID = :id";
        command.Parameters.Add("id", applicationId);

        var rows = new List<(long, int, string, string)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3)));
        }

        return rows;
    }
}
