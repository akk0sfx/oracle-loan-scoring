using ScoringApi.Core.Scoring;

namespace ScoringApi.Data;

/// <summary>Access to the Oracle scoring package and log.</summary>
public interface IScoringRepository
{
    /// <summary>Runs PKG_LOAN_SCORING.EVALUATE and commits the SCORING_LOG row.</summary>
    Task<StoredEvaluation> EvaluateAsync(string applicationId, ScoringInput input, CancellationToken cancellationToken);

    /// <summary>SCORING_LOG rows of the application, newest first. Empty if none.</summary>
    Task<IReadOnlyList<ScoringHistoryEntry>> GetHistoryAsync(string applicationId, CancellationToken cancellationToken);
}

public sealed record StoredEvaluation(long LogId, ScoringOutput Output);

public sealed record ScoringHistoryEntry(
    long Id,
    int Score,
    Decision Decision,
    decimal? Rate,
    decimal? MonthlyPayment,
    DateTime CreatedAtUtc);
