using ScoringApi.Core.Scoring;
using ScoringApi.Data;

namespace ScoringApi.UnitTests.Api;

/// <summary>
/// In-memory IScoringRepository. By default it scores with the reference calculator; a test can
/// set <see cref="Failure"/> to make every call throw instead.
/// </summary>
public sealed class FakeScoringRepository : IScoringRepository
{
    private readonly List<(string ApplicationId, ScoringInput Input)> _calls = [];

    public Func<Exception>? Failure { get; set; }

    public IReadOnlyList<ScoringHistoryEntry> History { get; set; } = [];

    public IReadOnlyList<(string ApplicationId, ScoringInput Input)> Calls => _calls;

    public Task<StoredEvaluation> EvaluateAsync(string applicationId, ScoringInput input, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            throw Failure();
        }

        _calls.Add((applicationId, input));
        return Task.FromResult(new StoredEvaluation(_calls.Count, ScoringCalculator.Evaluate(input)));
    }

    public Task<IReadOnlyList<ScoringHistoryEntry>> GetHistoryAsync(string applicationId, CancellationToken cancellationToken) =>
        Failure is not null ? throw Failure() : Task.FromResult(History);
}
