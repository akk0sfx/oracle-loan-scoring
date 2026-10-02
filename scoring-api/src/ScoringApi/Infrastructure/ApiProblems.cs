using Microsoft.AspNetCore.Mvc;
using ScoringApi.Core.Validation;

namespace ScoringApi.Infrastructure;

/// <summary>Builders for RFC 9457 problem details with the contract "code" extension.</summary>
public static class ApiProblems
{
    public static ProblemDetails Create(int status, string code, string title, string? detail = null)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions[ErrorCodes.ExtensionName] = code;
        return problem;
    }

    /// <summary>400 with per-field "errors", grouped by camelCase JSON field name.</summary>
    public static HttpValidationProblemDetails Validation(IEnumerable<ValidationError> errors)
    {
        var byField = errors
            .GroupBy(e => e.Field, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray(), StringComparer.Ordinal);

        var problem = new HttpValidationProblemDetails(byField)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
        };
        problem.Extensions[ErrorCodes.ExtensionName] = ErrorCodes.ValidationError;
        return problem;
    }
}
