namespace ScoringApi.Contracts;

/// <summary>
/// Body of POST /api/v1/scoring/evaluate (CONTRACTS.md, section 4).
/// Fields are nullable so a missing field becomes a 400 with a per-field error instead of a default value.
/// </summary>
public sealed record EvaluateRequest(
    string? ApplicationId,
    decimal? Amount,
    int? TermMonths,
    decimal? MonthlyIncome,
    string? PurposeCode);
