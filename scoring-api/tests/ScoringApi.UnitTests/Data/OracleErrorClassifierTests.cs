using ScoringApi.Data;

namespace ScoringApi.UnitTests.Data;

public sealed class OracleErrorClassifierTests
{
    [Theory]
    [InlineData(20001)]
    [InlineData(20003)]
    [InlineData(20005)]
    public void PackageValidationCodes(int number) =>
        OracleErrorClassifier.Classify(number).ShouldBe(OracleErrorKind.PackageValidation);

    [Theory]
    [InlineData(12541)] // no listener
    [InlineData(12514)] // unknown service
    [InlineData(1017)]  // invalid credentials
    [InlineData(50201)] // ODP.NET Managed: failed to connect (verified with Oracle stopped)
    public void ConnectivityCodes(int number) =>
        OracleErrorClassifier.Classify(number).ShouldBe(OracleErrorKind.Connectivity);

    [Theory]
    [InlineData(20000)] // internal invariant of the package, not user input
    [InlineData(20006)]
    [InlineData(942)]   // table or view does not exist
    [InlineData(1)]     // unique constraint violated
    public void OtherCodes(int number) =>
        OracleErrorClassifier.Classify(number).ShouldBe(OracleErrorKind.Other);

    [Theory]
    [InlineData("ORA-20001: Loan amount must be between 50000 and 5000000, got: 1\nORA-06512: at \"SCORING.PKG_LOAN_SCORING\", line 160",
        "Loan amount must be between 50000 and 5000000, got: 1")]
    [InlineData("plain message", "plain message")]
    public void FirstMessageLine_StripsOraPrefixAndStack(string message, string expected) =>
        OracleErrorClassifier.FirstMessageLine(message).ShouldBe(expected);
}
