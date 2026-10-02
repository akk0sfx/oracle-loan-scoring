using ScoringApi.Core.Scoring;

namespace ScoringApi.Contracts;

/// <summary>Element of GET /api/v1/scoring/{applicationId}/history (CONTRACTS.md, section 4).</summary>
public sealed record HistoryItemResponse(
    long Id,
    int Score,
    Decision Decision,
    decimal? Rate,
    decimal? MonthlyPayment,
    DateTime CreatedAt);
