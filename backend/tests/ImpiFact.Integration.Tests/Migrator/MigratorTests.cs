using Dapper;
using ImpiFact.Migrator;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace ImpiFact.Integration.Tests.Migrator;

/// <summary>
/// ImpiFact.Migrator aplica las migraciones y sincroniza los catálogos antes de cada despliegue (Arquitectura
/// detallada, sección 13). Se prueba por su punto de entrada, con la salida y el código de salida que vería quien
/// lo corre.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class MigratorTests
{
    [Test]
    public async Task Migrate_creates_the_schema_and_the_catalogs_on_an_empty_server()
    {
        var connectionString = MigratorDatabase.NewDatabase();

        var (exitCode, output, _) = await RunAsync(connectionString, "migrate");

        Assert.That(exitCode, Is.Zero);
        Assert.That(output, Does.Match(@"Migrations applied: \d{14}_InitialSchema"));
        Assert.That(output, Does.Match(@"Catalogs synchronized: \d+ inserted, 0 updated"));
        await using var connection = new NpgsqlConnection(connectionString);
        Assert.That(await connection.ExecuteScalarAsync<int>("select count(*) from municipalities"), Is.EqualTo(1122));
        Assert.That(await connection.ExecuteScalarAsync<int>("select count(*) from roles"), Is.EqualTo(5));
    }

    [Test]
    public async Task Migrate_twice_changes_nothing_the_second_time()
    {
        var connectionString = MigratorDatabase.NewDatabase();
        await RunAsync(connectionString, "migrate");

        var (exitCode, output, _) = await RunAsync(connectionString, "migrate");

        Assert.That(exitCode, Is.Zero);
        Assert.That(output, Does.Contain("Migrations applied: none"));
        Assert.That(output, Does.Contain("Catalogs synchronized: 0 inserted, 0 updated"));
    }

    [Test]
    public async Task An_unknown_command_prints_the_usage_and_exits_with_2()
    {
        var (exitCode, _, error) = await RunAsync(MigratorDatabase.NewDatabase(), "drop-everything");

        Assert.That(exitCode, Is.EqualTo(2));
        Assert.That(error, Does.Contain("Usage: ImpiFact.Migrator migrate"));
    }

    [Test]
    public async Task Without_a_connection_string_it_exits_with_1_and_names_the_setting()
    {
        var (exitCode, _, error) = await RunAsync(connectionString: null, "migrate");

        Assert.That(exitCode, Is.EqualTo(1));
        Assert.That(error, Does.Contain("ConnectionString"));
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string? connectionString, params string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Postgres",
                ["Database:ConnectionString"] = connectionString,
            })
            .Build();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await MigratorApp.RunAsync(args, configuration, output, error, CancellationToken.None);

        return (exitCode, output.ToString(), error.ToString());
    }
}
