using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using ScoringApi.Options;

namespace ScoringApi.Infrastructure;

/// <summary>Requires a valid X-Api-Key header on /api/v1/* endpoints (CONTRACTS.md, section 4).</summary>
public sealed class ApiKeyEndpointFilter(IOptions<ScoringOptions> options) : IEndpointFilter
{
    public const string HeaderName = "X-Api-Key";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var provided = context.HttpContext.Request.Headers[HeaderName].ToString();

        if (!KeysMatch(provided, options.Value.ApiKey))
        {
            return TypedResults.Problem(ApiProblems.Create(
                StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized,
                "Unauthorized", $"Missing or invalid {HeaderName} header."));
        }

        return await next(context);
    }

    /// <summary>
    /// Constant-time comparison. FixedTimeEquals returns early when lengths differ, which would
    /// leak the key length; hashing both values first makes the inputs always 32 bytes long.
    /// </summary>
    private static bool KeysMatch(string provided, string expected)
    {
        if (provided.Length == 0)
        {
            return false;
        }

        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
    }
}
