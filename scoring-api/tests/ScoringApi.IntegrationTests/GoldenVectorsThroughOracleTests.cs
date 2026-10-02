using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ScoringApi.Tests.Shared;

namespace ScoringApi.IntegrationTests;

/// <summary>
/// The main test of the project: every golden vector goes through the real HTTP API into
/// PKG_LOAN_SCORING in Oracle, and the PL/SQL result must equal the expected values that the
/// C# reference produced. If PL/SQL and C# ever diverge (rounding, thresholds, order of
/// reasons), this fails.
/// </summary>
[Collection(OracleCollection.Name)]
[Trait("Category", "Integration")]
public sealed class GoldenVectorsThroughOracleTests(OracleFixture oracle) : IAsyncLifetime
{
    private OracleApiFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new OracleApiFactory(oracle.ConnectionString);
        _client = _factory.CreateAuthorizedClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Theory]
    [MemberData(nameof(GoldenVectors.Names), MemberType = typeof(GoldenVectors))]
    public async Task Evaluate_InOracle_MatchesGoldenVector(string name)
    {
        var vector = GoldenVectors.Get(name);

        using var response = await _client.PostAsJsonAsync("/api/v1/scoring/evaluate", new
        {
            applicationId = Guid.NewGuid().ToString(),
            amount = vector.Input.Amount,
            termMonths = vector.Input.TermMonths,
            monthlyIncome = vector.Input.MonthlyIncome,
            purposeCode = vector.Input.PurposeCode,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var expected = vector.Expected;

        root.GetProperty("score").GetInt32().ShouldBe(expected.Score);
        root.GetProperty("decision").GetString().ShouldBe(expected.Decision);
        NullableDecimal(root.GetProperty("rate")).ShouldBe(expected.Rate);
        NullableDecimal(root.GetProperty("monthlyPayment")).ShouldBe(expected.MonthlyPayment);
        root.GetProperty("maxApprovedAmount").GetDecimal().ShouldBe(expected.MaxApprovedAmount);
        root.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()).ShouldBe(expected.Reasons);
    }

    private static decimal? NullableDecimal(JsonElement element) =>
        element.ValueKind == JsonValueKind.Null ? null : element.GetDecimal();
}
