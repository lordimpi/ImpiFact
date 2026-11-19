
namespace ImpiFact.Integration.Tests.Migrator;

/// <summary>
/// Servidor de base de datos (PostgreSQL o MySQL) en un contenedor de Docker (Testcontainers) para las pruebas del Migrator. Cada prueba usa
/// una base nueva con nombre propio: el Migrator la crea al aplicar las migraciones, así se prueba también el caso
/// de una instalación desde cero.
/// </summary>
[SetUpFixture]
[Category("Integration")]
public sealed class MigratorDatabase
{
    private static TestDatabaseContainer? container;

    /// <summary>Cadena de conexión a una base que todavía no existe en el servidor de pruebas.</summary>
    public static string NewDatabase() =>
        TestDatabase.WithDatabase(
            (container ?? throw new InvalidOperationException("The database container is not running.")).GetConnectionString(),
            "migrator_" + Guid.NewGuid().ToString("N"));

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        container = TestDatabase.CreateContainer();
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
