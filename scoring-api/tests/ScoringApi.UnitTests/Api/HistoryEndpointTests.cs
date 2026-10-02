using System.Net;
using System.Text.Json;
using ScoringApi.Core.Scoring;
using ScoringApi.Data;

namespace ScoringApi.UnitTests.Api;

public sealed class HistoryEndpointTests
{
    private const string Url = "/api/v1/scoring/6f1c2b9e-0000-0000-0000-000000000001/history";

    [Fact]
    public async Task History_ReturnsArray_WithContractFields()
    {
        await using var factory = new ApiFactory();
        factory.Repository.History =
        [
            new ScoringHistoryEntry(12, 790, Decision.Approve, 20.9m, 25740.12m, new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc)),
            new ScoringHistoryEntry(11, 320, Decision.Reject, null, null, new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc)),
        ];
        using var client = factory.CreateAuthorizedClient();

        using var response = await client.GetAsync(Url);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var items = json.RootElement.EnumerateArray().ToArray();
        items.Length.ShouldBe(2);
        items[0].PropertyNames().ShouldBe(["id", "score", "decision", "rate", "monthlyPayment", "createdAt"], ignoreOrder: true);
        items[0].GetProperty("id").GetInt64().ShouldBe(12);
        items[0].GetProperty("decision").GetString().ShouldBe("APPROVE");
        items[0].GetProperty("createdAt").GetString().ShouldBe("2026-10-02T12:00:00Z");
        items[1].GetProperty("rate").ValueKind.ShouldBe(JsonValueKind.Null);
        body.ShouldContain("\"rate\":20.90");
    }

    [Fact]
    public async Task History_NoRecords_ReturnsEmptyArray()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();

        var body = await client.GetStringAsync(Url);

        body.ShouldBe("[]");
    }

    [Fact]
    public async Task History_InvalidApplicationId_Returns400()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();

        using var response = await client.GetAsync("/api/v1/scoring/not-a-guid/history");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.ShouldBeProblem(400, "VALIDATION_ERROR");
        json.RootElement.GetProperty("errors").PropertyNames().ShouldBe(["applicationId"]);
    }
}
