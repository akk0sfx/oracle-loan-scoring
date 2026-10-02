namespace ScoringApi.Core.Scoring;

/// <summary>
/// Reference C# implementation of the scoring algorithm (CONTRACTS.md, section 5).
/// Must produce exactly the same numbers as PKG_LOAN_SCORING in Oracle; the shared
/// expectations live in tests/golden-vectors.json.
/// </summary>
public static class ScoringCalculator
{
    private const decimal DtiBaseRate = 20.00m;
    private const int BaseScore = 600;
    private const decimal DtiLowLimit = 0.30m;
    private const decimal DtiHighLimit = 0.50m;
    private const int LongTermLimit = 60;
    private const decimal LargeAmountLimit = 3_000_000m;
    private const int MinScore = 0;
    private const int MaxScore = 1000;
    private const int ApproveThreshold = 700;
    private const int ReviewThreshold = 500;
    private const int HighDiscountScore = 800;
    private const decimal MaxPaymentShare = 0.40m;
    private const decimal MaxAmountStep = 1000m;
    private const decimal MaxAmountCap = 5_000_000m;

    public static ScoringOutput Evaluate(ScoringInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!PurposeCodes.BaseRates.TryGetValue(input.PurposeCode, out var baseRate))
        {
            throw new ArgumentException($"Unknown purpose code '{input.PurposeCode}'.", nameof(input));
        }

        var reasons = new List<string>(4);

        // Steps 1–2. Not rounded: the contract rounds only the final payment (ADR-005).
        var basePayment = Annuity(input.Amount, DtiBaseRate, input.TermMonths);
        var dti = basePayment / input.MonthlyIncome;

        // Step 3. Rule order defines the reasons order.
        var score = BaseScore;
        if (dti < DtiLowLimit)
        {
            score += 200;
            reasons.Add("DTI_LOW");
        }
        else if (dti <= DtiHighLimit)
        {
            score += 50;
            reasons.Add("DTI_MEDIUM");
        }
        else
        {
            score -= 250;
            reasons.Add("DTI_HIGH");
        }

        if (input.TermMonths > LongTermLimit)
        {
            score -= 50;
            reasons.Add("LONG_TERM");
        }

        if (input.Amount > LargeAmountLimit)
        {
            score -= 50;
            reasons.Add("LARGE_AMOUNT");
        }

        score += PurposeBonus(input.PurposeCode);
        reasons.Add("PURPOSE_" + input.PurposeCode);
        score = Math.Clamp(score, MinScore, MaxScore);

        // Step 4.
        var decision = score >= ApproveThreshold ? Decision.Approve
            : score >= ReviewThreshold ? Decision.Review
            : Decision.Reject;

        if (decision == Decision.Reject)
        {
            return new ScoringOutput(score, decision, Rate: null, MonthlyPayment: null, MaxApprovedAmount: 0m, reasons);
        }

        // Step 5.
        var discount = score >= HighDiscountScore ? 2.00m : score >= ApproveThreshold ? 1.00m : 0m;
        var rate = baseRate - discount;

        // Step 6.
        var monthlyPayment = RoundMoney(Annuity(input.Amount, rate, input.TermMonths));

        // Step 7. The annuity is linear in the amount, so the inverse is a division.
        var rawMax = MaxPaymentShare * input.MonthlyIncome / Annuity(1m, rate, input.TermMonths);
        var maxApprovedAmount = Math.Min(decimal.Floor(rawMax / MaxAmountStep) * MaxAmountStep, MaxAmountCap);

        return new ScoringOutput(score, decision, rate, monthlyPayment, maxApprovedAmount, reasons);
    }

    /// <summary>
    /// Annuity payment, not rounded: r = rate / 12 / 100; amount * r / (1 - (1 + r)^(-term)).
    /// </summary>
    public static decimal Annuity(decimal amount, decimal ratePercent, int termMonths)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(termMonths);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ratePercent);

        var r = ratePercent / 12m / 100m;
        // (1 + r)^(-n) = 1 / (1 + r)^n. Integer power in decimal, see PowInt (ADR-006).
        return amount * r / (1m - (1m / PowInt(1m + r, termMonths)));
    }

    /// <summary>Money rounding: 2 decimals, half away from zero (same as Oracle ROUND).</summary>
    public static decimal RoundMoney(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// x^n for a non-negative integer n by exponentiation by squaring, entirely in decimal.
    /// decimal has no Pow, and Math.Pow(double) would lose precision (~15–17 digits) and break
    /// parity with Oracle NUMBER (~38 digits). Squaring needs O(log n) multiplications, so the
    /// rounding error of decimal (28–29 digits) stays far below a kopeck.
    /// </summary>
    internal static decimal PowInt(decimal x, int n)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(n);

        var result = 1m;
        while (n > 0)
        {
            if ((n & 1) == 1)
            {
                result *= x;
            }

            x *= x;
            n >>= 1;
        }

        return result;
    }

    private static int PurposeBonus(string purposeCode) => purposeCode switch
    {
        PurposeCodes.Mortgage => 50,
        PurposeCodes.Car => 30,
        PurposeCodes.Refinance => 10,
        PurposeCodes.Consumer => 0,
        PurposeCodes.Other => -30,
        _ => throw new ArgumentException($"No bonus defined for purpose '{purposeCode}'.", nameof(purposeCode)),
    };
}
