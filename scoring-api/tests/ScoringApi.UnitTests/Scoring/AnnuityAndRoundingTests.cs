using ScoringApi.Core.Scoring;

namespace ScoringApi.UnitTests.Scoring;

public sealed class AnnuityAndRoundingTests
{
    [Fact]
    public void Annuity_KnownValue_100000At12PercentFor12Months()
    {
        // 100 000 * 0.01 / (1 - 1.01^-12) = 8884.8788678341707...; also checked in Oracle tests.
        var payment = ScoringCalculator.Annuity(100_000m, 12m, 12);

        decimal.Round(payment, 10).ShouldBe(8884.8788678342m);
        ScoringCalculator.RoundMoney(payment).ShouldBe(8884.88m);
    }

    [Fact]
    public void Annuity_IsNotRoundedInternally()
    {
        var payment = ScoringCalculator.Annuity(100_000m, 12m, 12);

        payment.ShouldNotBe(ScoringCalculator.RoundMoney(payment));
    }

    [Fact]
    public void Annuity_IsLinearInAmount() =>
        // calc_max_amount relies on this: payment(amount) = amount * payment(1).
        ScoringCalculator.Annuity(1_000_000m, 17.9m, 36)
            .ShouldBe(1_000_000m * ScoringCalculator.Annuity(1m, 17.9m, 36), tolerance: 0.000001m);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Annuity_RejectsNonPositiveTerm(int term) =>
        Should.Throw<ArgumentOutOfRangeException>(() => ScoringCalculator.Annuity(100_000m, 12m, term));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Annuity_RejectsNonPositiveRate(int rate) =>
        Should.Throw<ArgumentOutOfRangeException>(() => ScoringCalculator.Annuity(100_000m, rate, 12));

    [Theory]
    // Midpoints go away from zero (Oracle ROUND); banker's rounding would give 2.34 / 0.12 / -2.34.
    [InlineData("2.345", "2.35")]
    [InlineData("0.125", "0.13")]
    [InlineData("-2.345", "-2.35")]
    [InlineData("2.344", "2.34")]
    [InlineData("2.3449999", "2.34")]
    [InlineData("2.35", "2.35")]
    public void RoundMoney_UsesMidpointAwayFromZero(string value, string expected) =>
        ScoringCalculator.RoundMoney(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture))
            .ShouldBe(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Evaluate_RejectsUnknownPurpose() =>
        Should.Throw<ArgumentException>(() => ScoringCalculator.Evaluate(new ScoringInput(100_000m, 12, 100_000m, "GOLD")));

    [Fact]
    public void Evaluate_Reject_HasNullRateAndPaymentAndZeroMaxAmount()
    {
        var output = ScoringCalculator.Evaluate(new ScoringInput(2_000_000m, 24, 100_000m, "OTHER"));

        output.Decision.ShouldBe(Decision.Reject);
        output.Rate.ShouldBeNull();
        output.MonthlyPayment.ShouldBeNull();
        output.MaxApprovedAmount.ShouldBe(0m);
    }
}
