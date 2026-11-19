using System.Net;
using ImpiFact.Application.Abstractions;
using ImpiFact.Infrastructure;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Platform.Outbox;
using ImpiFact.Infrastructure.Tenancy;
using ImpiFact.Integration.Tests.Identity;
using ImpiFact.Integration.Tests.Protection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Integration.Tests.Startup;

/// <summary>
/// La API y el Worker no arrancan sobre una base con migraciones pendientes (<see cref="PendingMigrationsGuard"/>):
/// escriben una sola línea con las que faltan y terminan. Caso real: el Worker arrancó sobre una base sin
/// <c>password_reset_tokens</c> y la tarea diaria Housekeeping falló con «42P01: relation "password_reset_tokens" does
/// not exist». Un PostgreSQL desechable con dos bases: una con todas las migraciones y otra vacía.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class PendingMigrationsStartupTests
{
    private const string EmptyDatabase = "impifact_empty";

    private TestDatabaseContainer _container = null!;
    private string _migrated = null!;
    private string _empty = null!;
    private string _expectedLine = null!;

    [OneTimeSetUp]
    public async Task StartDatabaseAsync()
    {
        _container = TestDatabase.CreateContainer();
        await _container.StartAsync();
        _migrated = _container.GetConnectionString();
        _empty = await TestDatabase.CreateDatabaseAsync(_migrated, EmptyDatabase);

        await using var services = DatabaseServices(_migrated);
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(companyId: null, userId: null, ActorType.System);
        var db = scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();
        await db.Database.MigrateAsync();
        _expectedLine = "La base de datos tiene migraciones pendientes: " + string.Join(", ", db.Database.GetMigrations()) +
            ". Corra el Migrator (dotnet run --project src/ImpiFact.Migrator) antes de arrancar.";
    }

    [OneTimeTearDown]
    public async Task StopDatabaseAsync() => await _container.DisposeAsync();

    [Test]
    public void The_api_does_not_start_on_a_database_with_pending_migrations()
    {
        using var scratch = new ProtectionScratch();
        var logs = new CapturingLoggerProvider();
        using var factory = Api(scratch, logs, _empty);

        Assert.Catch(() => factory.CreateClient().GetAsync("/health/live").GetAwaiter().GetResult());

        Assert.That(CriticalLines(logs), Is.EqualTo(new[] { _expectedLine }));
    }

    [Test]
    public async Task The_api_starts_on_a_database_with_every_migration_applied()
    {
        using var scratch = new ProtectionScratch();
        var logs = new CapturingLoggerProvider();
        await using var factory = Api(scratch, logs, _migrated);

        using var response = await factory.CreateClient().GetAsync("/health/live");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(CriticalLines(logs), Is.Empty);
        });
    }

    [Test]
    public async Task The_check_can_be_turned_off_for_hosts_that_create_the_schema_after_starting()
    {
        using var scratch = new ProtectionScratch();
        var logs = new CapturingLoggerProvider();
        await using var factory = Api(scratch, logs, _empty, ("Database:CheckPendingMigrationsAtStartup", "false"));

        using var response = await factory.CreateClient().GetAsync("/health/live");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(CriticalLines(logs), Is.Empty);
        });
    }

    // La revisión es de esquema, no de disponibilidad: con la base caída la API arranca como antes (la prueba de vida
    // no depende de la base) y lo deja escrito como advertencia.
    [Test]
    public async Task With_an_unreachable_database_the_api_still_starts_and_warns_that_it_could_not_check()
    {
        using var scratch = new ProtectionScratch();
        var logs = new CapturingLoggerProvider();
        await using var factory = Api(scratch, logs, UnreachableDatabase.ConnectionString);

        using var response = await factory.CreateClient().GetAsync("/health/live");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(CriticalLines(logs), Is.Empty);
            Assert.That(logs.Entries, Has.One.StartsWith(
                "Warning ImpiFact.Startup: No se pudo revisar si la base de datos tiene migraciones pendientes"));
        });
    }

    [Test]
    public async Task The_worker_does_not_start_on_a_database_with_pending_migrations()
    {
        using var scratch = new ProtectionScratch();
        var logs = new CapturingLoggerProvider();
        using var host = Worker(scratch, logs, _empty);
        using var safety = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var exitCode = await host.RunWhenDatabaseIsMigratedAsync(safety.Token);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(safety.IsCancellationRequested, Is.False, "It exits right away instead of running until stopped.");
            Assert.That(CriticalLines(logs), Is.EqualTo(new[] { _expectedLine }));
            Assert.That(logs.Entries, Has.None.Contains("42P01"), "No background job ran against the incomplete schema.");
        });
    }

    [Test]
    public async Task The_worker_runs_on_a_database_with_every_migration_applied_until_it_is_stopped()
    {
        using var scratch = new ProtectionScratch();
        var logs = new CapturingLoggerProvider();
        using var host = Worker(scratch, logs, _migrated);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var exitCode = await host.RunWhenDatabaseIsMigratedAsync(stop.Token);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(stop.IsCancellationRequested, Is.True, "It ran until it was stopped.");
            Assert.That(CriticalLines(logs), Is.Empty);
        });
    }

    private static string[] CriticalLines(CapturingLoggerProvider logs) =>
        [.. logs.Entries
            .Where(entry => entry.StartsWith("Critical ", StringComparison.Ordinal))
            .Select(entry => entry["Critical ImpiFact.Startup: ".Length..].Split(" | ")[0])];

    private static WebApplicationFactory<Program> Api(
        ProtectionScratch scratch, CapturingLoggerProvider logs, string connectionString, params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:ConnectionString", connectionString)
                .UseSetting("Secrets:LocalAesGcm:MasterKeyFile", scratch.MasterKeyFile)
                .UseSetting("Secrets:LocalAesGcm:CertificateDirectory", scratch.CertificateDirectory)
                .UseSetting("Storage:Local:Root", Path.Combine(scratch.Root, "storage"))
                .ConfigureLogging(logging => logging.AddProvider(logs));
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });

    /// <summary>El Worker como lo arma <c>ImpiFact.Worker</c>: su appsettings.json y <c>AddWorkerServices</c>.</summary>
    private static IHost Worker(ProtectionScratch scratch, CapturingLoggerProvider logs, string connectionString)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Development" });
        builder.Configuration.AddJsonFile(WorkerSettingsFile(), optional: false);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(scratch.LocalAesGcmSettings())
        {
            ["Database:ConnectionString"] = connectionString,
            ["Storage:Local:Root"] = Path.Combine(scratch.Root, "storage"),
            ["Panel:BaseUrl"] = "https://panel.impifact.test",
            ["Email:Host"] = "smtp.platform.test",
            ["Email:FromAddress"] = "no-reply@platform.test",
        });
        builder.Logging.AddProvider(logs);
        // Como ImpiFact.Worker: sin revisar al construir los casos de uso de la API que el Worker no usa.
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false }));
        builder.Services.AddWorkerServices(builder.Configuration, TestDatabase.Providers);
        return builder.Build();
    }

    private static ServiceProvider DatabaseServices(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = TestDatabase.ProviderName,
                ["Database:ConnectionString"] = connectionString,
            })
            .Build();
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddInfrastructure(configuration, TestDatabase.Providers);
        return collection.BuildServiceProvider();
    }

    private static string WorkerSettingsFile()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ImpiFact.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("The backend folder was not found."),
            "src", "ImpiFact.Worker", "appsettings.json");
    }
}
