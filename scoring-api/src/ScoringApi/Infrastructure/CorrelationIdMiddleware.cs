namespace ScoringApi.Infrastructure;

/// <summary>
/// Accepts X-Correlation-Id or generates one, echoes it in the response and puts it into the
/// logging scope, so every log line of the request carries the id (CONTRACTS.md, section 4).
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";
    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        // The id ends up in logs and response headers, so arbitrary client input is not trusted:
        // anything long or with unexpected characters is replaced (log/header injection).
        var correlationId = IsValid(incoming) ? incoming : Guid.NewGuid().ToString();

        context.Items[HeaderName] = correlationId;
        // Set right before the headers are sent, not now: UseExceptionHandler clears the
        // response (headers included) before writing the error, which would drop the id.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        // A message-template scope is rendered by both console formatters: as text by "simple"
        // and as a structured "CorrelationId" property by "json".
        using (logger.BeginScope("CorrelationId:{CorrelationId}", correlationId))
        {
            await next(context);
        }
    }

    private static bool IsValid(string value) =>
        value.Length is > 0 and <= MaxLength && AllowedChars().IsMatch(value);

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z0-9._:-]+$")]
    private static partial System.Text.RegularExpressions.Regex AllowedChars();
}
