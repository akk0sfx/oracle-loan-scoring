using ScoringApi.Core.Scoring;

namespace ScoringApi.Contracts;

/// <summary>Response 200 of POST /api/v1/scoring/evaluate (CONTRACTS.md, section 4).</summary>
public sealed record EvaluateResponse(
    string ApplicationId,
    int Score,
    Decision Decision,
    decimal? Rate,
    decimal? MonthlyPayment,
    decimal MaxApprovedAmount,
    IReadOnlyList<string> Reasons,
    DateTime EvaluatedAt);
