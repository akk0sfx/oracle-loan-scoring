using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ScoringApi.Data;

namespace ScoringApi.UnitTests.Api;

public sealed class ErrorHandlingTests
{
    private static readonly object ValidRequest = new
    {
        applicationId = "6f1c2b9e-0000-0000-0000-000000000001",
        amount = 500000m, termMonths = 24, monthlyIncome = 120000m, purposeCode = "CONSUMER",
    };

    [Fact]
    public async Task DatabaseUnavailable_Returns503()
    {
        await using var factory = new ApiFactory();
        factory.Repository.Failure = () => new ScoringDatabaseUnavailableException(50201, new IOException("connection refused"));
        using var client = factory.CreateAuthorizedClient();

        using var response = await client.PostAsJsonAsync("/api/v1/scoring/evaluate", ValidRequest);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.ShouldBeProblem(503, "DATABASE_UNAVAILABLE");
    }

    [Fact]
    public async Task PackageRejection_Returns400_WithPackageMessage()
    {
        await using var factory = new ApiFactory();
        factory.Repository.Failure = () => new ScoringInputRejectedException(20004, "Unknown loan purpose: X", new InvalidOperationException());
        using var client = factory.CreateAuthorizedClient();

        using var response = await client.PostAsJsonAsync("/api/v1/scoring/evaluate", ValidRequest);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.ShouldBeProblem(400, "VALIDATION_ERROR");
        json.RootElement.GetProperty("detail").GetString().ShouldBe("Unknown loan purpose: X");
    }

    [Fact]
    public async Task UnexpectedException_Returns500_WithoutDetails()
    {
        const string secret = "SECRET-connection-string-Password=hunter2";
        await using var factory = new ApiFactory();
        factory.Repository.Failure = () => new InvalidOperationException(secret);
        using var client = factory.CreateAuthorizedClient();

        using var response = await client.PostAsJsonAsync("/api/v1/scoring/evaluate", ValidRequest);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        json.RootElement.ShouldBeProblem(500, "INTERNAL_ERROR");
        body.ShouldNotContain(secret);
        body.ShouldNotContain(nameof(InvalidOperationException));
        body.ShouldNotContain("   at "); // stack trace frame
        json.RootElement.TryGetProperty("detail", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task UnknownRoute_Returns404_WithCode()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateAuthorizedClient();

        using var response = await client.GetAsync("/api/v1/nope");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("NOT_FOUND");
    }
}
