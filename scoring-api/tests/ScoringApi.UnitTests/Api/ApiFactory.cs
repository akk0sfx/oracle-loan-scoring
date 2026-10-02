using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ScoringApi.Data;

namespace ScoringApi.UnitTests.Api;

/// <summary>
/// Hosts the real API pipeline in memory with the repository replaced by a fake: no Oracle,
/// no network. Each test creates its own factory, so fake state never leaks between tests.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string ApiKey = "unit-test-key";

    public FakeScoringRepository Repository { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Production: the same error handling and logging configuration as in the container.
        builder.UseEnvironment("Production");
        builder.UseSetting("Scoring:ApiKey", ApiKey);
        builder.UseSetting("ConnectionStrings:Oracle", "Data Source=unused-in-unit-tests");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IScoringRepository>();
            services.AddSingleton<IScoringRepository>(Repository);
            // No database: the Oracle probe is removed; /health then reports Healthy.
            services.Configure<HealthCheckServiceOptions>(o => o.Registrations.Clear());
        });
    }

    public HttpClient CreateAuthorizedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        return client;
    }
}
