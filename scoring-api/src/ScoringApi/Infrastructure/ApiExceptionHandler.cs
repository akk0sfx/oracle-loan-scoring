using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;

namespace ScoringApi.Infrastructure;

/// <summary>
/// Turns unhandled exceptions into contract problem details (CONTRACTS.md, section 4):
/// PKG_LOAN_SCORING validation errors -> 400, connectivity -> 503, everything else -> 500
/// without exception details in the response.
/// </summary>
public sealed partial class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    // RAISE_APPLICATION_ERROR codes of PKG_LOAN_SCORING (CONTRACTS.md, section 6).
    // OracleException.Number holds the positive ORA number.
    private const int FirstPackageValidationError = 20001;
    private const int LastPackageValidationError = 20005;

    // Errors meaning "the database cannot be reached or used right now".
    // TODO(verify): the full list for ODP.NET Managed — docs/OPEN_QUESTIONS.md, Q-005.
    private static readonly HashSet<int> ConnectivityErrors =
    [
        1017,   // invalid username/password; logon denied
        1033,   // ORACLE initialization or shutdown in progress
        1034,   // ORACLE not available
        1089,   // immediate shutdown in progress
        1109,   // database not open
        3113,   // end-of-file on communication channel
        3114,   // not connected to ORACLE
        3135,   // connection lost contact
        12154,  // could not resolve the connect identifier
        12170,  // connect timeout occurred
        12514,  // listener does not currently know of service requested
        12516,  // listener could not find available handler
        12520,  // listener could not find available handler for requested type of server
        12528,  // listener: all appropriate instances are blocking new connections
        12537,  // connection closed
        12541,  // no listener
        12543,  // destination host unreachable
        12545,  // connect failed because target host or object does not exist
        12560,  // protocol adapter error
        12571,  // packet writer failure
        28000,  // the account is locked
        // ODP.NET Managed wraps network failures: the outer OracleException carries 50201 and the
        // TNS error (e.g. ORA-12537) is only in the inner NetworkException. Observed with Oracle stopped.
        50201,  // Oracle Communication: failed to connect to server
        // TODO(verify): connection pool timeout ("Connection request timed out") — Q-005.
        50000,
    ];

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

            case OracleException { Number: >= FirstPackageValidationError and <= LastPackageValidationError } ora:
                // The message is our own text from the package, safe to return to the caller.
                // It may contain input values, so it is not logged.
                LogPackageValidation(logger, ora.Number);
                return ApiProblems.Create(StatusCodes.Status400BadRequest, ErrorCodes.ValidationError,
                    "Validation failed in the scoring package.", FirstMessageLine(ora));

            case OracleException ora when ConnectivityErrors.Contains(ora.Number):
                LogDatabaseUnavailable(logger, exception, ora.Number);
                return ApiProblems.Create(StatusCodes.Status503ServiceUnavailable, ErrorCodes.DatabaseUnavailable,
                    "Database unavailable.", "The scoring database is temporarily unavailable. Retry later.");

            default:
                LogUnhandled(logger, exception);
                return ApiProblems.Create(StatusCodes.Status500InternalServerError, ErrorCodes.InternalError,
                    "Internal server error.");
        }
    }

    /// <summary>"ORA-20001: Loan amount must be ...\nORA-06512: at ..." -> "Loan amount must be ...".</summary>
    private static string FirstMessageLine(OracleException exception)
    {
        var firstLine = exception.Message.Split('\n', 2)[0].Trim();
        var prefixEnd = firstLine.IndexOf(": ", StringComparison.Ordinal);
        return firstLine.StartsWith("ORA-", StringComparison.Ordinal) && prefixEnd > 0
            ? firstLine[(prefixEnd + 2)..]
            : firstLine;
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
