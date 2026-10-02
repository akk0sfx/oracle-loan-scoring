using System.Text.Json;

namespace ScoringApi.UnitTests.Api;

internal static class JsonAssertions
{
    public static string[] PropertyNames(this JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name).ToArray();

    /// <summary>Problem details per RFC 9457 with the contract "code" extension.</summary>
    public static void ShouldBeProblem(this JsonElement problem, int status, string code)
    {
        problem.GetProperty("status").GetInt32().ShouldBe(status);
        problem.GetProperty("code").GetString().ShouldBe(code);
        problem.TryGetProperty("title", out _).ShouldBeTrue();
        problem.TryGetProperty("type", out _).ShouldBeTrue();
    }
}
