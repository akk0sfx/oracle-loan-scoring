using Microsoft.AspNetCore.Http.HttpResults;
using ScoringApi.Contracts;
using ScoringApi.Core.Scoring;
using ScoringApi.Core.Validation;
using ScoringApi.Data;
using ScoringApi.Infrastructure;

namespace ScoringApi.Endpoints;

/// <summary>/api/v1/scoring/* endpoints (CONTRACTS.md, section 4).</summary>
public static partial class ScoringEndpoints
{
    private const string EvaluateDescription = """
        Scores a loan application with the Oracle package PKG_LOAN_SCORING and logs the result.
        Requires the `X-Api-Key` header. Input is validated before the database call.

        Example request:

        ```json
        {
          "applicationId": "6f1c2b9e-0000-0000-0000-000000000001",
          "amount": 500000.00,
          "termMonths": 24,
          "monthlyIncome": 120000.00,
          "purposeCode": "CONSUMER"
        }
        ```

        Example response:

        ```json
        {
          "applicationId": "6f1c2b9e-0000-0000-0000-000000000001",
          "score": 800,
          "decision": "APPROVE",
          "rate": 19.90,
          "monthlyPayment": 25423.48,
          "maxApprovedAmount": 944000.00,
          "reasons": ["DTI_LOW", "PURPOSE_CONSUMER"],
          "evaluatedAt": "2026-10-02T12:00:00Z"
        }
        ```

        For REJECT: `rate` and `monthlyPayment` are null, `maxApprovedAmount` is 0.
        """;

    private const string HistoryDescription = """
        Returns all SCORING_LOG rows of the application, newest first; an empty array if there are none.
        Requires the `X-Api-Key` header.

        Example: `GET /api/v1/scoring/6f1c2b9e-0000-0000-0000-000000000001/history`
        """;

    public static RouteGroupBuilder MapScoringEndpoints(this RouteGroupBuilder api)
    {
        var scoring = api.MapGroup("/scoring").WithTags("Scoring");

        scoring.MapPost("/evaluate", EvaluateAsync)
            .WithName("EvaluateApplication")
            .WithSummary("Evaluate a loan application")
            .WithDescription(EvaluateDescription)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        scoring.MapGet("/{applicationId}/history", GetHistoryAsync)
            .WithName("GetScoringHistory")
            .WithSummary("Scoring history of an application")
            .WithDescription(HistoryDescription)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return api;
    }

    private static async Task<Results<Ok<EvaluateResponse>, ValidationProblem>> EvaluateAsync(
        EvaluateRequest request,
        IScoringRepository repository,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var errors = LoanApplicationValidator.Validate(
            request.ApplicationId, request.Amount, request.TermMonths, request.MonthlyIncome, request.PurposeCode);
        if (errors.Count > 0)
        {
            return ToValidationProblem(errors);
        }

        // Validation guarantees non-null values below.
        var input = new ScoringInput(request.Amount!.Value, request.TermMonths!.Value, request.MonthlyIncome!.Value, request.PurposeCode!);
        var stored = await repository.EvaluateAsync(request.ApplicationId!, input, cancellationToken);
        var output = stored.Output;

        // Only identifiers and the outcome are logged — never amounts or income (personal data).
        LogEvaluated(loggerFactory.CreateLogger(typeof(ScoringEndpoints)),
            request.ApplicationId!, stored.LogId, output.Decision.ToCode(), output.Score);

        return TypedResults.Ok(new EvaluateResponse(
            request.ApplicationId!,
            output.Score,
            output.Decision,
            Money.Scale2(output.Rate),
            Money.Scale2(output.MonthlyPayment),
            Money.Scale2(output.MaxApprovedAmount),
            output.Reasons,
            timeProvider.GetUtcNow().UtcDateTime));
    }

    private static async Task<Results<Ok<HistoryItemResponse[]>, ValidationProblem>> GetHistoryAsync(
        string applicationId,
        IScoringRepository repository,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(applicationId, out _))
        {
            return ToValidationProblem([new ValidationError("applicationId", "Application id must be a GUID.")]);
        }

        var history = await repository.GetHistoryAsync(applicationId, cancellationToken);
        return TypedResults.Ok(history
            .Select(h => new HistoryItemResponse(
                h.Id, h.Score, h.Decision, Money.Scale2(h.Rate), Money.Scale2(h.MonthlyPayment), h.CreatedAtUtc))
            .ToArray());
    }

    private static ValidationProblem ToValidationProblem(IReadOnlyList<ValidationError> errors)
    {
        var problem = ApiProblems.Validation(errors);
        return TypedResults.ValidationProblem(
            problem.Errors, title: problem.Title, extensions: problem.Extensions);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Application {ApplicationId} scored: log {LogId}, decision {Decision}, score {Score}")]
    private static partial void LogEvaluated(ILogger logger, string applicationId, long logId, string decision, int score);
}
