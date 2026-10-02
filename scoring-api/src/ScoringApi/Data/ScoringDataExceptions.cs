namespace ScoringApi.Data;

/// <summary>The scoring package rejected the input (ORA-20001..-20005). Message is safe to return to the caller.</summary>
public sealed class ScoringInputRejectedException(int oraNumber, string message, Exception innerException)
    : Exception(message, innerException)
{
    public int OraNumber { get; } = oraNumber;
}

/// <summary>The scoring database is unreachable or not usable right now.</summary>
public sealed class ScoringDatabaseUnavailableException(int oraNumber, Exception innerException)
    : Exception($"Scoring database unavailable (ORA-{oraNumber}).", innerException)
{
    public int OraNumber { get; } = oraNumber;
}
