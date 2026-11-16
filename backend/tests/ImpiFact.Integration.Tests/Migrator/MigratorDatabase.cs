using Testcontainers.PostgreSql;

namespace ImpiFact.Integration.Tests.Migrator;

/// <summary>
/// Servidor PostgreSQL en un contenedor de Docker (Testcontainers) para las pruebas del Migrator. Cada prueba usa
/// una base nueva con nombre propio: el Migrator la crea al aplicar las migraciones, así se prueba también el caso
/// de una instalación desde cero.
/// </summary>
[SetUpFixture]
[Category("Integration")]
public sealed class MigratorDatabase
{
    private static PostgreSqlContainer? container;

    /// <summary>Cadena de conexión a una base que todavía no existe en el servidor de pruebas.</summary>
    public static string NewDatabase() =>
        (container ?? throw new InvalidOperationException("The PostgreSQL container is not running."))
            .GetConnectionString()
            .Replace("Database=postgres", "Database=migrator_" + Guid.NewGuid().ToString("N"), StringComparison.Ordinal);

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        container = new PostgreSqlBuilder("postgres:18.1-alpine").Build();
        await container.StartAsync();
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
