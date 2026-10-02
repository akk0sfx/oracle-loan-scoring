using ScoringApi.Core.Scoring;
using ScoringApi.Tests.Shared;

namespace ScoringApi.UnitTests.Scoring;

public sealed class ScoringCalculatorGoldenVectorTests
{
    [Fact]
    public void GoldenVectors_FileHasEnoughCases() => GoldenVectors.All.Count.ShouldBeInRange(20, 30);

    [Theory]
    [MemberData(nameof(GoldenVectors.Names), MemberType = typeof(GoldenVectors))]
    public void Evaluate_MatchesGoldenVector(string name)
    {
        var vector = GoldenVectors.Get(name);
        var input = new ScoringInput(vector.Input.Amount, vector.Input.TermMonths, vector.Input.MonthlyIncome, vector.Input.PurposeCode);

        var output = ScoringCalculator.Evaluate(input);

        output.Score.ShouldBe(vector.Expected.Score);
        output.Decision.ToCode().ShouldBe(vector.Expected.Decision);
        output.Rate.ShouldBe(vector.Expected.Rate);
        output.MonthlyPayment.ShouldBe(vector.Expected.MonthlyPayment);
        output.MaxApprovedAmount.ShouldBe(vector.Expected.MaxApprovedAmount);
        output.Reasons.ShouldBe(vector.Expected.Reasons);
    }
}
