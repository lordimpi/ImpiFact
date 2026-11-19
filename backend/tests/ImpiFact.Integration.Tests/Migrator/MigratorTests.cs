using System.Security.Cryptography;
using System.Text;
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
        await using var connection = TestDatabase.Connect(connectionString);
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

    // La primera API key de plataforma de una instalación nueva (supuesto A5): se muestra una sola vez y solo su
    // resumen SHA-256 queda en la base.
    [Test]
    public async Task Create_platform_key_prints_the_key_once_and_stores_only_its_hash()
    {
        var connectionString = MigratorDatabase.NewDatabase();
        await RunAsync(connectionString, "migrate");

        var (exitCode, output, _) = await RunAsync(connectionString, "create-platform-key", "Instalación inicial");

        Assert.That(exitCode, Is.Zero);
        var plain = output.Split('\n', StringSplitOptions.TrimEntries).Single(line => line.StartsWith("apk_", StringComparison.Ordinal));
        await using var connection = TestDatabase.Connect(connectionString);
        var row = await connection.QuerySingleAsync<(Guid? CompanyId, string Name, string Scopes, string KeyHash)>(
            "select company_id, name, scopes, key_hash from api_keys");
        Assert.Multiple(() =>
        {
            Assert.That(row.CompanyId, Is.Null);
            Assert.That(row.Name, Is.EqualTo("Instalación inicial"));
            Assert.That(row.Scopes, Is.EqualTo("platform:*"));
            Assert.That(row.KeyHash, Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plain)))));
            Assert.That(row.KeyHash, Does.Not.Contain(plain));
        });
    }

    [Test]
    public async Task Create_platform_key_without_a_name_prints_the_usage()
    {
        var (exitCode, _, error) = await RunAsync(MigratorDatabase.NewDatabase(), "create-platform-key");

        Assert.That(exitCode, Is.EqualTo(2));
        Assert.That(error, Does.Contain("create-platform-key <name>"));
    }

    [Test]
    public async Task An_unknown_command_prints_the_usage_and_exits_with_2()
    {
        var (exitCode, _, error) = await RunAsync(MigratorDatabase.NewDatabase(), "drop-everything");

        Assert.That(exitCode, Is.EqualTo(2));
        Assert.That(error, Does.Contain("Usage: ImpiFact.Migrator migrate"));
        Assert.That(error, Does.Contain("create-platform-key <name>"));
    }

    [Test]
    public async Task Without_a_connection_string_it_exits_with_1_and_names_the_setting()
    {
        var (exitCode, _, error) = await RunAsync(connectionString: null, "migrate");

        Assert.That(exitCode, Is.EqualTo(1));
        Assert.That(error, Does.Contain("ConnectionString"));
    }

    // Database:Provider elige el motor entre los disponibles; sin él o con uno que no existe, el comando no toca la base.
    [TestCase(null, "Database:Provider is not set.")]
    [TestCase("Oracle", "Database provider 'Oracle' is not available.")]
    public async Task Without_an_available_provider_it_exits_with_1_and_lists_the_available_ones(string? provider, string message)
    {
        var (exitCode, output, error) = await RunWithProviderAsync("Host=localhost;Database=impifact", provider, "migrate");

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(output, Is.Empty);
            Assert.That(error.Trim(), Is.EqualTo($"Invalid configuration: {message} {TestDatabase.AvailableProvidersMessage}"));
        });
    }

    private static Task<(int ExitCode, string Output, string Error)> RunAsync(string? connectionString, params string[] args) =>
        RunWithProviderAsync(connectionString, TestDatabase.ProviderName, args);

    private static async Task<(int ExitCode, string Output, string Error)> RunWithProviderAsync(
        string? connectionString, string? provider, params string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = provider,
                ["Database:ConnectionString"] = connectionString,
            })
            .Build();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await MigratorApp.RunAsync(args, configuration, output, error, CancellationToken.None);

        return (exitCode, output.ToString(), error.ToString());
    }
}
