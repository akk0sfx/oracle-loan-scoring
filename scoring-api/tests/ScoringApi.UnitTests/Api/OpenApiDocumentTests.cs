using System.Text.Json;

namespace ScoringApi.UnitTests.Api;

public sealed class OpenApiDocumentTests
{
    [Fact]
    public async Task Document_ListsContractPaths_AndApiKeyScheme()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var json = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var root = json.RootElement;

        root.GetProperty("paths").PropertyNames().ShouldBe(
            ["/api/v1/scoring/evaluate", "/api/v1/scoring/{applicationId}/history"], ignoreOrder: true);

        var scheme = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("ApiKey");
        scheme.GetProperty("type").GetString().ShouldBe("apiKey");
        scheme.GetProperty("in").GetString().ShouldBe("header");
        scheme.GetProperty("name").GetString().ShouldBe("X-Api-Key");

        var evaluate = root.GetProperty("paths").GetProperty("/api/v1/scoring/evaluate").GetProperty("post");
        evaluate.GetProperty("security").EnumerateArray().ShouldHaveSingleItem().TryGetProperty("ApiKey", out _).ShouldBeTrue();
    }
}
