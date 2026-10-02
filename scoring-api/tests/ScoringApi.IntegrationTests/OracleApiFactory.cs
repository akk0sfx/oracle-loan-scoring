using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ScoringApi.IntegrationTests;

/// <summary>The real API with the real OracleScoringRepository, pointed at the given database.</summary>
public sealed class OracleApiFactory(string connectionString, Action<IServiceCollection>? configureServices = null)
    : WebApplicationFactory<Program>
{
    public const string ApiKey = "integration-test-key";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Scoring:ApiKey", ApiKey);
        builder.UseSetting("ConnectionStrings:Oracle", connectionString);
        if (configureServices is not null)
        {
            builder.ConfigureTestServices(configureServices);
        }
    }

    public HttpClient CreateAuthorizedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        return client;
    }
}
