using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ScoringApi.UnitTests.Api;

public sealed class EvaluateEndpointTests
{
    private const string Url = "/api/v1/scoring/evaluate";
    private const string ApplicationId = "6f1c2b9e-0000-0000-0000-000000000001";

    private static object ValidRequest(string purposeCode = "CONSUMER") => new
    {
        applicationId = ApplicationId,
        amount = 500000.00m,
        termMonths = 24,
        monthlyIncome = 120000.00m,
        purposeCode,
    };

    [Fact]
    public async Task ValidRequest_Returns200_WithExactContractShape()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();

        using var response = await client.PostAsJsonAsync(Url, ValidRequest());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        // Field names exactly as in CONTRACTS.md section 4, nothing more, nothing less.
        root.PropertyNames().ShouldBe(
            ["applicationId", "score", "decision", "rate", "monthlyPayment", "maxApprovedAmount", "reasons", "evaluatedAt"],
            ignoreOrder: true);

        root.GetProperty("applicationId").GetString().ShouldBe(ApplicationId);
        root.GetProperty("score").GetInt32().ShouldBe(800);
        root.GetProperty("decision").GetString().ShouldBe("APPROVE");
        root.GetProperty("rate").GetDecimal().ShouldBe(19.90m);
        root.GetProperty("monthlyPayment").GetDecimal().ShouldBe(25423.48m);
        root.GetProperty("maxApprovedAmount").GetDecimal().ShouldBe(944000m);
        root.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()).ShouldBe(["DTI_LOW", "PURPOSE_CONSUMER"]);
        root.GetProperty("evaluatedAt").GetString().ShouldEndWith("Z");

        // Money is serialized with two decimals, like the contract examples.
        body.ShouldContain("\"rate\":19.90");
        body.ShouldContain("\"maxApprovedAmount\":944000.00");

        factory.Repository.Calls.ShouldHaveSingleItem().ApplicationId.ShouldBe(ApplicationId);
    }

    [Fact]
    public async Task RejectDecision_HasNullRateAndPayment_AndZeroMaxAmount()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();

        using var response = await client.PostAsJsonAsync(Url, new
        {
            applicationId = ApplicationId, amount = 2000000m, termMonths = 24, monthlyIncome = 100000m, purposeCode = "OTHER",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("decision").GetString().ShouldBe("REJECT");
        json.RootElement.GetProperty("rate").ValueKind.ShouldBe(JsonValueKind.Null);
        json.RootElement.GetProperty("monthlyPayment").ValueKind.ShouldBe(JsonValueKind.Null);
        json.RootElement.GetProperty("maxApprovedAmount").GetDecimal().ShouldBe(0m);
    }

    [Fact]
    public async Task InvalidFields_Return400_WithErrorsPerField_AndDoNotReachRepository()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();

        using var response = await client.PostAsJsonAsync(Url, new
        {
            applicationId = "", amount = 10m, termMonths = 120, monthlyIncome = 0m, purposeCode = "GOLD",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.ShouldBeProblem(400, "VALIDATION_ERROR");
        json.RootElement.GetProperty("errors").PropertyNames().ShouldBe(
            ["applicationId", "amount", "termMonths", "monthlyIncome", "purposeCode"], ignoreOrder: true);
        factory.Repository.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task MissingFields_Return400_WithRequiredErrors()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();

        using var response = await client.PostAsJsonAsync(Url, new { applicationId = ApplicationId });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("errors").PropertyNames().ShouldBe(
            ["amount", "termMonths", "monthlyIncome", "purposeCode"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("{\"applicationId\":\"6f1c2b9e-0000-0000-0000-000000000001\",\"termMonths\":\"twenty\"}")]
    [InlineData("{ not json")]
    public async Task MalformedBody_Returns400_ValidationError(string body)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();

        using var response = await client.PostAsync(Url, new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.ShouldBeProblem(400, "VALIDATION_ERROR");
    }
}
