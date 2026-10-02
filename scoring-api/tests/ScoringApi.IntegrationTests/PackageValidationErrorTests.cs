using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ScoringApi.Core.Scoring;
using ScoringApi.Data;
using ScoringApi.Options;

namespace ScoringApi.IntegrationTests;

/// <summary>
/// ORA-20001..-20005 raised by PKG_LOAN_SCORING must become 400 VALIDATION_ERROR.
/// The API validates input before the database, so a valid request is sent and a decorator
/// corrupts it on the way to the real repository — the error then really comes from Oracle.
/// </summary>
[Collection(OracleCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PackageValidationErrorTests(OracleFixture oracle)
{
    public static TheoryData<int, string> Cases => new()
    {
        { 20001, "Loan amount must be between" },
        { 20002, "Loan term must be a whole number" },
        { 20003, "Monthly income must be greater than 0" },
        { 20004, "Unknown loan purpose" },
        { 20005, "Application id (application_id) is empty" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task PackageError_BecomesValidationError400(int oraNumber, string messageStart)
    {
        await using var factory = new OracleApiFactory(oracle.ConnectionString, services =>
        {
            services.RemoveAll<IScoringRepository>();
            services.AddSingleton<IScoringRepository>(sp => new CorruptingRepository(
                new OracleScoringRepository(sp.GetRequiredService<IOptions<OracleOptions>>()), oraNumber));
        });
        using var client = factory.CreateAuthorizedClient();

        using var response = await client.PostAsJsonAsync("/api/v1/scoring/evaluate", new
        {
            applicationId = Guid.NewGuid().ToString(), amount = 500000m, termMonths = 24, monthlyIncome = 120000m, purposeCode = "CONSUMER",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("VALIDATION_ERROR");
        json.RootElement.GetProperty("detail").GetString().ShouldStartWith(messageStart);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Repository_TranslatesPackageError(int oraNumber, string messageStart)
    {
        var repository = new CorruptingRepository(
            new OracleScoringRepository(Microsoft.Extensions.Options.Options.Create(new OracleOptions { ConnectionString = oracle.ConnectionString })),
            oraNumber);

        var ex = await Should.ThrowAsync<ScoringInputRejectedException>(() =>
            repository.EvaluateAsync(Guid.NewGuid().ToString(), new ScoringInput(500000m, 24, 120000m, "CONSUMER"), CancellationToken.None));

        ex.OraNumber.ShouldBe(oraNumber);
        ex.Message.ShouldStartWith(messageStart);
    }

    /// <summary>Breaks exactly the field that the given ORA code validates.</summary>
    private sealed class CorruptingRepository(IScoringRepository inner, int oraNumber) : IScoringRepository
    {
        public Task<StoredEvaluation> EvaluateAsync(string applicationId, ScoringInput input, CancellationToken cancellationToken) =>
            oraNumber switch
            {
                20001 => inner.EvaluateAsync(applicationId, input with { Amount = 1m }, cancellationToken),
                20002 => inner.EvaluateAsync(applicationId, input with { TermMonths = 120 }, cancellationToken),
                20003 => inner.EvaluateAsync(applicationId, input with { MonthlyIncome = 0m }, cancellationToken),
                20004 => inner.EvaluateAsync(applicationId, input with { PurposeCode = "GOLD" }, cancellationToken),
                20005 => inner.EvaluateAsync(string.Empty, input, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(oraNumber)),
            };

        public Task<IReadOnlyList<ScoringHistoryEntry>> GetHistoryAsync(string applicationId, CancellationToken cancellationToken) =>
            inner.GetHistoryAsync(applicationId, cancellationToken);
    }
}
