using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Json;
using Scalar.AspNetCore;
using ScoringApi.Data;
using ScoringApi.Endpoints;
using ScoringApi.Health;
using ScoringApi.Infrastructure;
using ScoringApi.OpenApi;
using ScoringApi.Options;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration: secrets come from environment variables, validated at startup ----
builder.Services.AddOptions<ScoringOptions>()
    .Bind(builder.Configuration.GetSection(ScoringOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<OracleOptions>()
    .Configure(o => o.ConnectionString = builder.Configuration.GetConnectionString("Oracle") ?? string.Empty)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ---- Errors: RFC 9457 problem details with the contract "code" extension everywhere ----
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions.TryAdd(ErrorCodes.ExtensionName, ErrorCodes.ForStatus(context.ProblemDetails.Status)));
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
// By default minimal APIs swallow body binding failures as an empty 400 outside Development.
// Throwing lets ApiExceptionHandler answer with VALIDATION_ERROR problem details instead.
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);

builder.Services.Configure<JsonOptions>(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

// ---- Services ----
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IScoringRepository, OracleScoringRepository>();
builder.Services.AddHealthChecks()
    .AddCheck<OracleHealthCheck>("oracle", timeout: TimeSpan.FromSeconds(5));
builder.Services.AddOpenApi(o => o.AddDocumentTransformer<ApiKeySecuritySchemeTransformer>());

var app = builder.Build();

// Correlation id first, so the exception handler's log lines are inside its scope.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
// Empty error responses (unknown route, wrong method) become problem details too.
app.UseStatusCodePages();

app.MapOpenApi(); // /openapi/v1.json
if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference(); // /scalar
}

app.MapHealthChecks("/health", new HealthCheckOptions
{
    // Contract format: {"status":"Healthy"} / {"status":"Unhealthy"}; check details are not exposed.
    ResponseWriter = (context, report) =>
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new { status = report.Status.ToString() }));
    },
});

app.MapGroup("/api/v1")
    .AddEndpointFilter<ApiKeyEndpointFilter>()
    .MapScoringEndpoints();

app.Run();

// Makes Program visible to WebApplicationFactory<Program> in integration tests.
public partial class Program;
