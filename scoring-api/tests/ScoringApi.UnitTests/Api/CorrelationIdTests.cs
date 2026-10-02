using System.Net.Http.Json;

namespace ScoringApi.UnitTests.Api;

public sealed class CorrelationIdTests
{
    private const string Header = "X-Correlation-Id";

    [Fact]
    public async Task ProvidedId_IsEchoed()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add(Header, "corr-123");

        using var response = await client.SendAsync(request);

        response.Headers.GetValues(Header).ShouldHaveSingleItem().ShouldBe("corr-123");
    }

    [Fact]
    public async Task MissingId_IsGenerated()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Guid.TryParse(response.Headers.GetValues(Header).ShouldHaveSingleItem(), out _).ShouldBeTrue();
    }

    [Fact]
    public async Task UnsafeId_IsReplaced()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.TryAddWithoutValidation(Header, new string('a', 65));

        using var response = await client.SendAsync(request);

        Guid.TryParse(response.Headers.GetValues(Header).ShouldHaveSingleItem(), out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Id_IsReturned_OnErrorResponses()
    {
        // UseExceptionHandler clears response headers; the middleware sets the id in OnStarting.
        await using var factory = new ApiFactory();
        factory.Repository.Failure = () => new InvalidOperationException("boom");
        using var client = factory.CreateAuthorizedClient();
        client.DefaultRequestHeaders.Add(Header, "corr-error");

        using var response = await client.PostAsJsonAsync("/api/v1/scoring/evaluate", new
        {
            applicationId = "6f1c2b9e-0000-0000-0000-000000000001",
            amount = 500000m, termMonths = 24, monthlyIncome = 120000m, purposeCode = "CONSUMER",
        });

        ((int)response.StatusCode).ShouldBe(500);
        response.Headers.GetValues(Header).ShouldHaveSingleItem().ShouldBe("corr-error");
    }
}
