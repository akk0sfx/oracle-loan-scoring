using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;

namespace ScoringApi.IntegrationTests;

/// <summary>
/// One gvenzl/oracle-free container for the whole "Oracle" test collection. The container is
/// initialized exactly like docker-compose: oracle/init is mounted into /container-entrypoint-initdb.d,
/// so the image runs the same scripts in the same (alphabetical) order.
/// </summary>
public sealed class OracleFixture : IAsyncLifetime
{
    private const string Image = "gvenzl/oracle-free:23-slim";
    private const ushort OraclePort = 1521;
    private const string AppUserPassword = "IntegrationTests123";
    private const string ReadyMarker = "DATABASE IS READY TO USE!";

    private readonly IContainer _container = new ContainerBuilder(Image)
        .WithEnvironment("ORACLE_PASSWORD", "IntegrationSys123")
        .WithEnvironment("APP_USER", "SCORING")
        .WithEnvironment("APP_USER_PASSWORD", AppUserPassword)
        .WithBindMount(Path.Combine(RepositoryRoot(), "oracle", "init"), "/container-entrypoint-initdb.d", AccessMode.ReadOnly)
        .WithPortBinding(OraclePort, assignRandomHostPort: true)
        // Printed by the image after the init scripts have run (same moment healthcheck.sh turns healthy).
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged(ReadyMarker))
        .Build();

    public string ConnectionString =>
        $"User Id=SCORING;Password={AppUserPassword};Data Source=//{_container.Hostname}:{_container.GetMappedPublicPort(OraclePort)}/FREEPDB1";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // The image does not fail the container when an init script fails, so check explicitly.
        // Only the part before the ready marker is ours; after it the image tails alert_FREE.log.
        var (stdout, _) = await _container.GetLogsAsync();
        var initLog = stdout.Split(ReadyMarker)[0];
        if (initLog.Contains("ORA-", StringComparison.Ordinal) || initLog.Contains("compilation errors", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Oracle init scripts reported errors:\n" + initLog);
        }
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docs", "CONTRACTS.md")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root (docs/CONTRACTS.md) not found.");
    }
}

[CollectionDefinition(Name)]
public sealed class OracleCollection : ICollectionFixture<OracleFixture>
{
    public const string Name = "Oracle";
}
