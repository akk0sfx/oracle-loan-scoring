using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ScoringApi.Data;

namespace ScoringApi.Infrastructure;

/// <summary>
/// Turns unhandled exceptions into contract problem details (CONTRACTS.md, section 4):
/// package validation errors -> 400, database unavailable -> 503, everything else -> 500
/// without exception details in the response. Oracle errors are already classified by the
/// data layer (ADR-012).
/// </summary>
public sealed partial class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = Map(exception);
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private ProblemDetails Map(Exception exception)
    {
        switch (exception)
        {
            // Malformed JSON or a wrong field type (e.g. "termMonths": "abc").
            case BadHttpRequestException:
                LogBadRequest(logger, exception.GetType().Name);
                return ApiProblems.Create(StatusCodes.Status400BadRequest, ErrorCodes.ValidationError,
                    "Invalid request body.", "The request body is not valid JSON or has fields of the wrong type.");

            case ScoringInputRejectedException rejected:
                // The message is our own text from the package, safe to return to the caller.
                // It may contain input values, so it is not logged.
                LogPackageValidation(logger, rejected.OraNumber);
                return ApiProblems.Create(StatusCodes.Status400BadRequest, ErrorCodes.ValidationError,
                    "Validation failed in the scoring package.", rejected.Message);

            case ScoringDatabaseUnavailableException unavailable:
                LogDatabaseUnavailable(logger, exception, unavailable.OraNumber);
                return ApiProblems.Create(StatusCodes.Status503ServiceUnavailable, ErrorCodes.DatabaseUnavailable,
                    "Database unavailable.", "The scoring database is temporarily unavailable. Retry later.");

            default:
                LogUnhandled(logger, exception);
                return ApiProblems.Create(StatusCodes.Status500InternalServerError, ErrorCodes.InternalError,
                    "Internal server error.");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected malformed request: {ExceptionType}")]
    private static partial void LogBadRequest(ILogger logger, string exceptionType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Scoring package rejected input with ORA-{OraNumber}")]
    private static partial void LogPackageValidation(ILogger logger, int oraNumber);

    [LoggerMessage(Level = LogLevel.Error, Message = "Database unavailable (ORA-{OraNumber})")]
    private static partial void LogDatabaseUnavailable(ILogger logger, Exception exception, int oraNumber);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception")]
    private static partial void LogUnhandled(ILogger logger, Exception exception);
}
