using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;
using ScoringApi.Options;

namespace ScoringApi.Health;

/// <summary>Database probe for /health: SELECT 1 FROM DUAL (CONTRACTS.md, section 4).</summary>
public sealed class OracleHealthCheck(IOptions<OracleOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new OracleConnection(options.Value.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM DUAL";
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is OracleException or InvalidOperationException or TimeoutException)
        {
            // The exception goes to the health check log, never to the /health response body.
            return new HealthCheckResult(context.Registration.FailureStatus, "Oracle is unreachable.", ex);
        }
    }
}
