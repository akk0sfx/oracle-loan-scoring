namespace ScoringApi.Contracts;

internal static class Money
{
    /// <summary>
    /// Normalizes a money/rate value to scale 2 so JSON shows 19.90 and 944000.00 as in the contract.
    /// Adding 0.00m keeps the value and raises the decimal scale to at least 2.
    /// </summary>
    public static decimal Scale2(decimal value) => value + 0.00m;

    public static decimal? Scale2(decimal? value) => value is { } v ? Scale2(v) : null;
}
