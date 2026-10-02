using ScoringApi.Core.Validation;

namespace ScoringApi.UnitTests.Validation;

public sealed class LoanApplicationValidatorTests
{
    private const string ValidId = "6f1c2b9e-0000-0000-0000-000000000001";

    private static IReadOnlyList<ValidationError> Validate(
        string? applicationId = ValidId, decimal? amount = 500_000m, int? termMonths = 24,
        decimal? monthlyIncome = 120_000m, string? purposeCode = "CONSUMER") =>
        LoanApplicationValidator.Validate(applicationId, amount, termMonths, monthlyIncome, purposeCode);

    private static void ShouldFailOnly(IReadOnlyList<ValidationError> errors, string field) =>
        errors.ShouldHaveSingleItem().Field.ShouldBe(field);

    [Fact]
    public void ValidInput_HasNoErrors() => Validate().ShouldBeEmpty();

    // ---- amount: 50 000 .. 5 000 000 inclusive ----
    [Theory]
    [InlineData("50000")]
    [InlineData("5000000")]
    [InlineData("1234567.89")]
    public void Amount_InsideRangeInclusive_IsValid(string amount) =>
        Validate(amount: decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)).ShouldBeEmpty();

    [Theory]
    [InlineData("49999.99")]
    [InlineData("5000000.01")]
    [InlineData("0")]
    [InlineData("-1")]
    public void Amount_OutsideRange_IsInvalid(string amount) =>
        ShouldFailOnly(Validate(amount: decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)), "amount");

    // ---- termMonths: 6 .. 84 inclusive ----
    [Theory]
    [InlineData(6)]
    [InlineData(84)]
    public void Term_BoundariesInclusive_AreValid(int term) => Validate(termMonths: term).ShouldBeEmpty();

    [Theory]
    [InlineData(5)]
    [InlineData(85)]
    [InlineData(0)]
    public void Term_OutsideRange_IsInvalid(int term) => ShouldFailOnly(Validate(termMonths: term), "termMonths");

    // ---- monthlyIncome: > 0 (exclusive) and <= 10 000 000 (inclusive) ----
    [Theory]
    [InlineData("0.01")]
    [InlineData("10000000")]
    public void Income_InsideRange_IsValid(string income) =>
        Validate(monthlyIncome: decimal.Parse(income, System.Globalization.CultureInfo.InvariantCulture)).ShouldBeEmpty();

    [Theory]
    [InlineData("0")]
    [InlineData("-100")]
    [InlineData("10000000.01")]
    public void Income_OutsideRange_IsInvalid(string income) =>
        ShouldFailOnly(Validate(monthlyIncome: decimal.Parse(income, System.Globalization.CultureInfo.InvariantCulture)), "monthlyIncome");

    // ---- purposeCode: one of the five codes, case-sensitive ----
    [Theory]
    [InlineData("CONSUMER")]
    [InlineData("CAR")]
    [InlineData("MORTGAGE")]
    [InlineData("REFINANCE")]
    [InlineData("OTHER")]
    public void Purpose_KnownCodes_AreValid(string code) => Validate(purposeCode: code).ShouldBeEmpty();

    [Theory]
    [InlineData("GOLD")]
    [InlineData("consumer")]
    [InlineData(" ")]
    public void Purpose_UnknownOrBlank_IsInvalid(string code) => ShouldFailOnly(Validate(purposeCode: code), "purposeCode");

    // ---- applicationId: required GUID ----
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("LA-000001")]
    public void ApplicationId_EmptyOrNotGuid_IsInvalid(string id) => ShouldFailOnly(Validate(applicationId: id), "applicationId");

    // ---- missing fields ----
    [Fact]
    public void MissingFields_AreReportedEach()
    {
        var errors = LoanApplicationValidator.Validate(null, null, null, null, null);

        errors.Select(e => e.Field).ShouldBe(["applicationId", "amount", "termMonths", "monthlyIncome", "purposeCode"]);
        errors.ShouldAllBe(e => e.Message.Contains("required"));
    }

    [Fact]
    public void SeveralViolations_AreAllReported() =>
        Validate(amount: 1m, termMonths: 100).Select(e => e.Field).ShouldBe(["amount", "termMonths"]);
}
