using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;
using ScoringApi.Core.Scoring;
using ScoringApi.Options;

namespace ScoringApi.Data;

/// <summary>
/// ODP.NET implementation. Stateless: every call opens a connection, which ODP.NET takes from
/// its pool, so the class is safe as a singleton.
/// </summary>
public sealed class OracleScoringRepository(IOptions<OracleOptions> options) : IScoringRepository
{
    private readonly string _connectionString = options.Value.ConnectionString;

    public Task<StoredEvaluation> EvaluateAsync(string applicationId, ScoringInput input, CancellationToken cancellationToken) =>
        TranslateErrorsAsync(() => EvaluateCoreAsync(applicationId, input, cancellationToken));

    public Task<IReadOnlyList<ScoringHistoryEntry>> GetHistoryAsync(string applicationId, CancellationToken cancellationToken) =>
        TranslateErrorsAsync(() => GetHistoryCoreAsync(applicationId, cancellationToken));

    /// <summary>
    /// Converts Oracle-specific failures into data-layer exceptions, so the HTTP layer
    /// (and its tests) does not depend on ODP.NET types (ADR-012).
    /// </summary>
    private static async Task<T> TranslateErrorsAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (OracleException ex)
        {
            switch (OracleErrorClassifier.Classify(ex.Number))
            {
                case OracleErrorKind.PackageValidation:
                    throw new ScoringInputRejectedException(ex.Number, OracleErrorClassifier.FirstMessageLine(ex.Message), ex);
                case OracleErrorKind.Connectivity:
                    throw new ScoringDatabaseUnavailableException(ex.Number, ex);
                default:
                    throw;
            }
        }
    }

    private async Task<StoredEvaluation> EvaluateCoreAsync(string applicationId, ScoringInput input, CancellationToken cancellationToken)
    {
        await using var connection = new OracleConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // EVALUATE does not commit by contract: the API owns the transaction and commits only
        // after all OUT values were read successfully.
        await using var transaction = (OracleTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "PKG_LOAN_SCORING.EVALUATE";
        command.CommandType = CommandType.StoredProcedure;
        // ODP.NET binds by position by default; by name is robust against parameter order.
        command.BindByName = true;
        command.Transaction = transaction;

        command.Parameters.Add("p_application_id", OracleDbType.Varchar2, applicationId, ParameterDirection.Input);
        command.Parameters.Add("p_amount", OracleDbType.Decimal, input.Amount, ParameterDirection.Input);
        command.Parameters.Add("p_term", OracleDbType.Int32, input.TermMonths, ParameterDirection.Input);
        command.Parameters.Add("p_income", OracleDbType.Decimal, input.MonthlyIncome, ParameterDirection.Input);
        command.Parameters.Add("p_purpose", OracleDbType.Varchar2, input.PurposeCode, ParameterDirection.Input);

        var score = command.Parameters.Add("o_score", OracleDbType.Decimal, ParameterDirection.Output);
        // VARCHAR2 OUT parameters need an explicit Size, otherwise ODP.NET fails or truncates.
        var decision = command.Parameters.Add("o_decision", OracleDbType.Varchar2, 10, null, ParameterDirection.Output);
        var rate = command.Parameters.Add("o_rate", OracleDbType.Decimal, ParameterDirection.Output);
        var payment = command.Parameters.Add("o_payment", OracleDbType.Decimal, ParameterDirection.Output);
        var maxAmount = command.Parameters.Add("o_max_amount", OracleDbType.Decimal, ParameterDirection.Output);
        var reasons = command.Parameters.Add("o_reasons", OracleDbType.Varchar2, 400, null, ParameterDirection.Output);
        var logId = command.Parameters.Add("o_log_id", OracleDbType.Decimal, ParameterDirection.Output);

        await command.ExecuteNonQueryAsync(cancellationToken);

        var output = new ScoringOutput(
            Score: (int)ToDecimal(score)!.Value,
            Decision: DecisionCodes.Parse(ToText(decision)!),
            Rate: ToDecimal(rate),
            MonthlyPayment: ToDecimal(payment),
            MaxApprovedAmount: ToDecimal(maxAmount) ?? 0m,
            Reasons: (ToText(reasons) ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries));
        var storedLogId = (long)ToDecimal(logId)!.Value;

        await transaction.CommitAsync(cancellationToken);
        return new StoredEvaluation(storedLogId, output);
    }

    private async Task<IReadOnlyList<ScoringHistoryEntry>> GetHistoryCoreAsync(string applicationId, CancellationToken cancellationToken)
    {
        // A plain SELECT instead of PKG_LOAN_SCORING.GET_HISTORY: Dapper has no built-in REF CURSOR
        // support (ADR-007). Columns and ordering mirror GET_HISTORY.
        const string sql = """
            SELECT ID, SCORE, DECISION, RATE, MONTHLY_PAYMENT AS MonthlyPayment, CREATED_AT AS CreatedAt
              FROM SCORING_LOG
             WHERE APPLICATION_ID = :applicationId
             ORDER BY CREATED_AT DESC, ID DESC
            """;

        await using var connection = new OracleConnection(_connectionString);
        var rows = await connection.QueryAsync<HistoryRow>(
            new CommandDefinition(sql, new { applicationId }, cancellationToken: cancellationToken));

        return rows.Select(r => new ScoringHistoryEntry(
                (long)r.Id,
                (int)r.Score,
                DecisionCodes.Parse(r.Decision),
                r.Rate,
                r.MonthlyPayment,
                // TIMESTAMP has no time zone; the container's database runs in UTC (ADR-008).
                DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc)))
            .ToList();
    }

    /// <summary>OUT NUMBER parameters come back as OracleDecimal; SQL NULL becomes null.</summary>
    private static decimal? ToDecimal(OracleParameter parameter) =>
        parameter.Value is OracleDecimal { IsNull: false } value ? value.Value : null;

    private static string? ToText(OracleParameter parameter) =>
        parameter.Value is OracleString { IsNull: false } value ? value.Value : null;

    /// <summary>
    /// Dapper materialization target. Oracle NUMBER maps to decimal, so numeric properties are
    /// decimal and converted afterwards; a class with setters avoids Dapper's exact-type
    /// constructor matching for records.
    /// </summary>
    private sealed class HistoryRow
    {
        public decimal Id { get; init; }
        public decimal Score { get; init; }
        public string Decision { get; init; } = string.Empty;
        public decimal? Rate { get; init; }
        public decimal? MonthlyPayment { get; init; }
        public DateTime CreatedAt { get; init; }
    }
}
