using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Built-in health checks: the Oracle check (SELECT 1 FROM DUAL) will be added here later,
// and /health will start returning 503 without touching the route.
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    // The default writer emits plain text "Healthy"; the contract requires JSON {"status":"Healthy"}.
    ResponseWriter = (context, report) =>
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(
            JsonSerializer.Serialize(new { status = report.Status.ToString() }));
    },
});

app.Run();

// Makes Program visible to WebApplicationFactory<Program> in integration tests.
public partial class Program;
