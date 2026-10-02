namespace ScoringApi.Core.Scoring;

/// <summary>Result of the scoring algorithm. For REJECT: Rate and MonthlyPayment are null, MaxApprovedAmount is 0.</summary>
public sealed record ScoringOutput(
    int Score,
    Decision Decision,
    decimal? Rate,
    decimal? MonthlyPayment,
    decimal MaxApprovedAmount,
    IReadOnlyList<string> Reasons);
