using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ScoringApi.UnitTests.Api;

public sealed class AuthenticationTests
{
    public static TheoryData<string?> BadKeys => new() { null, "", "wrong-key", ApiFactory.ApiKey + "x", ApiFactory.ApiKey.ToUpperInvariant() };

    [Theory]
    [MemberData(nameof(BadKeys))]
    public async Task Evaluate_WithoutValidKey_Returns401(string? key)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        if (key is not null)
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", key);
        }

        using var response = await client.PostAsJsonAsync("/api/v1/scoring/evaluate", new { applicationId = "x" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.ShouldBeProblem(401, "UNAUTHORIZED");
        factory.Repository.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task History_WithWrongKey_Returns401()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");

        using var response = await client.GetAsync("/api/v1/scoring/6f1c2b9e-0000-0000-0000-000000000001/history");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
