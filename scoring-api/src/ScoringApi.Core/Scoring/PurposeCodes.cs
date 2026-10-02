using System.Collections.Frozen;

namespace ScoringApi.Core.Scoring;

/// <summary>
/// Loan purpose codes and base rates (CONTRACTS.md, section 2.2).
/// The master copy lives in Oracle RATE_GRID; this one serves the reference calculator and validation.
/// </summary>
public static class PurposeCodes
{
    public const string Consumer = "CONSUMER";
    public const string Car = "CAR";
    public const string Mortgage = "MORTGAGE";
    public const string Refinance = "REFINANCE";
    public const string Other = "OTHER";

    /// <summary>Base rate, % per annum, by purpose code. Codes are case-sensitive, as in the contract.</summary>
    public static readonly FrozenDictionary<string, decimal> BaseRates = new Dictionary<string, decimal>
    {
        [Consumer] = 21.90m,
        [Car] = 17.90m,
        [Mortgage] = 14.50m,
        [Refinance] = 19.50m,
        [Other] = 24.90m,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static bool IsKnown(string? code) => code is not null && BaseRates.ContainsKey(code);
}
