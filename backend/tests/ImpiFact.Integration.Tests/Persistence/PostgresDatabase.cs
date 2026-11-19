
namespace ImpiFact.Integration.Tests.Persistence;

/// <summary>
/// Levanta un PostgreSQL desechable en Docker una sola vez para todas las pruebas de este espacio de nombres
/// y crea el esquema del contexto de prueba con <c>EnsureCreated</c> (crea las tablas a partir del modelo, sin
/// migraciones). Usa la misma imagen que <c>deploy/docker-compose.yml</c> y un puerto al azar, así no choca con
/// un PostgreSQL local.
/// </summary>
[SetUpFixture]
[Category("Integration")]
public sealed class PostgresDatabase
{
    private static TestDatabaseContainer? container;

    public static string ConnectionString =>
        container?.GetConnectionString() ?? throw new InvalidOperationException("The PostgreSQL container is not running.");

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        container = TestDatabase.CreateContainer();
        await container.StartAsync();

        using var harness = new PersistenceHarness();
        await using var session = harness.Open(companyId: null);
        await session.Db.Database.EnsureCreatedAsync();
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }
}
