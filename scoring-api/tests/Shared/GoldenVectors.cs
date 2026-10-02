using System.Text.Json;

namespace ScoringApi.Tests.Shared;

public sealed record GoldenInput(decimal Amount, int TermMonths, decimal MonthlyIncome, string PurposeCode);

public sealed record GoldenExpected(
    int Score,
    string Decision,
    decimal? Rate,
    decimal? MonthlyPayment,
    decimal MaxApprovedAmount,
    string[] Reasons);

public sealed record GoldenVector(string Name, GoldenInput Input, GoldenExpected Expected);

/// <summary>
/// Loads tests/golden-vectors.json (copied next to the test assembly). The vectors are shared by
/// the C# reference calculator tests and the API -> Oracle integration tests.
/// </summary>
public static class GoldenVectors
{
    private static readonly Lazy<IReadOnlyDictionary<string, GoldenVector>> Vectors = new(Load);

    public static IReadOnlyCollection<GoldenVector> All => Vectors.Value.Values.ToList();

    public static GoldenVector Get(string name) => Vectors.Value[name];

    /// <summary>
    /// Theory data: vector names only. xUnit v2 shows one test case per serializable row in the
    /// test explorer, and a string is serializable while a record is not.
    /// </summary>
    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var name in Vectors.Value.Keys)
        {
            data.Add(name);
        }

        return data;
    }

    private static IReadOnlyDictionary<string, GoldenVector> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "golden-vectors.json");
        var vectors = JsonSerializer.Deserialize<GoldenVector[]>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("golden-vectors.json is empty.");
        return vectors.ToDictionary(v => v.Name, StringComparer.Ordinal);
    }
}
