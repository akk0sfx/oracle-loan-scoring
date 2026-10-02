using System.Text.Json.Serialization;

namespace ScoringApi.Core.Scoring;

/// <summary>Scoring decision (CONTRACTS.md, section 5, step 4).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Decision>))]
public enum Decision
{
    [JsonStringEnumMemberName("APPROVE")]
    Approve,

    [JsonStringEnumMemberName("REVIEW")]
    Review,

    [JsonStringEnumMemberName("REJECT")]
    Reject,
}

/// <summary>Conversion between <see cref="Decision"/> and the wire/database codes.</summary>
public static class DecisionCodes
{
    public const string Approve = "APPROVE";
    public const string Review = "REVIEW";
    public const string Reject = "REJECT";

    public static string ToCode(this Decision decision) => decision switch
    {
        Decision.Approve => Approve,
        Decision.Review => Review,
        Decision.Reject => Reject,
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, null),
    };

    public static Decision Parse(string code) => code switch
    {
        Approve => Decision.Approve,
        Review => Decision.Review,
        Reject => Decision.Reject,
        _ => throw new FormatException($"Unknown decision code '{code}'."),
    };
}
