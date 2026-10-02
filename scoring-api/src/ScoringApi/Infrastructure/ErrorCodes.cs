namespace ScoringApi.Infrastructure;

/// <summary>Values of the ProblemDetails "code" extension (CONTRACTS.md, section 4).</summary>
public static class ErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string DatabaseUnavailable = "DATABASE_UNAVAILABLE";
    public const string InternalError = "INTERNAL_ERROR";

    // Not in the contract: framework-level errors (unknown route, wrong method). See OPEN_QUESTIONS Q-006.
    public const string NotFound = "NOT_FOUND";
    public const string MethodNotAllowed = "METHOD_NOT_ALLOWED";

    public const string ExtensionName = "code";

    /// <summary>Fallback code by HTTP status for problems that did not set one explicitly.</summary>
    public static string ForStatus(int? statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => ValidationError,
        StatusCodes.Status401Unauthorized => Unauthorized,
        StatusCodes.Status404NotFound => NotFound,
        StatusCodes.Status405MethodNotAllowed => MethodNotAllowed,
        StatusCodes.Status503ServiceUnavailable => DatabaseUnavailable,
        _ => InternalError,
    };
}
