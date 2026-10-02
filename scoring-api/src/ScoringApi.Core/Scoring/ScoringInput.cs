namespace ScoringApi.Core.Scoring;

/// <summary>Input of the scoring algorithm (CONTRACTS.md, section 5).</summary>
public sealed record ScoringInput(decimal Amount, int TermMonths, decimal MonthlyIncome, string PurposeCode);
