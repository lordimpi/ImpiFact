using System.Security.Cryptography;
using ImpiFact.Application;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Infrastructure;
using ImpiFact.Infrastructure.Identity;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Persistence.Seeding;
using ImpiFact.Infrastructure.Postgres;
using ImpiFact.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ImpiFact.Migrator;

/// <summary>
/// Comandos del Migrator. <c>migrate</c> aplica las migraciones pendientes (crea la base si no existe) y luego
/// sincroniza los catálogos DIAN desde los JSON de semilla. Es seguro correrlo varias veces: lo ya aplicado no se
/// repite. <c>create-platform-key &lt;nombre&gt;</c> crea una API key de plataforma (sin empresa, alcance
/// <c>platform:*</c>) para dar de alta las primeras empresas de una instalación nueva; la llave se muestra una sola
/// vez y en la base queda solo su resumen SHA-256. <c>seed-demo [--force]</c> crea o actualiza una empresa de prueba
/// lista para emitir en habilitación y muestra una API key nueva (ver <see cref="SeedDemo"/>); como crea datos
/// ficticios, solo corre en Development salvo que se pase <c>--force</c>.
/// </summary>
/// <remarks>
/// Códigos de salida: 0 si todo salió bien, 1 si falta configuración, falló la base o <c>seed-demo</c> no puede
/// correr, 2 si el comando no existe. Corre como actor de sistema (<see cref="ActorType.System"/>), que puede escribir
/// datos de cualquier empresa.
/// </remarks>
public static class MigratorApp
{
    private const string Usage =
        "Usage: ImpiFact.Migrator migrate | ImpiFact.Migrator create-platform-key <name> | ImpiFact.Migrator seed-demo [--force]";

    /// <summary>Variable que nombra el ambiente, la misma que lee el host genérico de .NET del Worker.</summary>
    private const string EnvironmentVariable = "DOTNET_ENVIRONMENT";

    public static Task<int> RunAsync(string[] args, IConfiguration configuration, TextWriter output, TextWriter error, CancellationToken ct) =>
        RunAsync(args, configuration, output, error, NewCertificatePassword, ct);

    /// <param name="newCertificatePassword">
    /// Genera la contraseña del certificado de <c>seed-demo</c>; las pruebas pasan una conocida para revisar que no se
    /// muestre.
    /// </param>
    public static async Task<int> RunAsync(
        string[] args, IConfiguration configuration, TextWriter output, TextWriter error, Func<string> newCertificatePassword, CancellationToken ct)
    {
        var command = args switch
        {
            ["migrate"] => Command.Migrate,
            ["create-platform-key", var name] when !string.IsNullOrWhiteSpace(name) => Command.CreatePlatformKey,
            ["seed-demo"] or ["seed-demo", "--force"] => Command.SeedDemo,
            _ => (Command?)null,
        };
        if (command is null)
        {
            await error.WriteLineAsync(Usage);
            return 2;
        }

        if (command == Command.SeedDemo)
        {
            if (!IsDevelopment(configuration) && args is not [_, "--force"])
            {
                await error.WriteLineAsync(
                    $"seed-demo crea datos ficticios y solo corre en Development ({EnvironmentVariable}=Development); el ambiente " +
                    $"actual es «{EnvironmentName(configuration)}». Si de verdad quiere crearlos aquí, agregue --force.");
                return 1;
            }

            configuration = WithSeedDemoDefaults(configuration);
        }

        ServiceProvider services;
        try
        {
            services = BuildServices(configuration, withApplication: command == Command.SeedDemo);
        }
        catch (InvalidOperationException exception)
        {
            // El registro solo falla por la configuración, por ejemplo sin Database:Provider o con un motor que no existe.
            await error.WriteLineAsync("Invalid configuration: " + exception.Message);
            return 1;
        }

        await using var disposeServices = services;
        try
        {
            // Valida la configuración de la base antes de tocarla: así el error nombra el dato que falta. seed-demo
            // además cifra el certificado y el PIN, así que necesita la llave maestra y la carpeta de certificados.
            _ = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            if (command == Command.SeedDemo)
            {
                _ = services.GetRequiredService<IOptions<SecretsOptions>>().Value;
            }
        }
        catch (OptionsValidationException exception)
        {
            await error.WriteLineAsync("Invalid configuration: " + string.Join(" ", exception.Failures));
            return 1;
        }

        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(companyId: null, userId: null, ActorType.System);
        var db = scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();

        if (command == Command.CreatePlatformKey)
        {
            return await CreatePlatformKeyAsync(scope.ServiceProvider, db, args[1].Trim(), output, ct);
        }

        if (command == Command.SeedDemo)
        {
            if ((await db.Database.GetPendingMigrationsAsync(ct)).Any())
            {
                await error.WriteLineAsync("La base tiene migraciones pendientes: corra primero «ImpiFact.Migrator migrate».");
                return 1;
            }

            return await new SeedDemo(services, output, error, newCertificatePassword).RunAsync(ct);
        }

        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        await db.Database.MigrateAsync(ct);
        await output.WriteLineAsync("Migrations applied: " + (pending.Count == 0 ? "none" : string.Join(", ", pending)));

        var report = await new CatalogSynchronizer(db).SynchronizeAsync(ct);
        await output.WriteLineAsync($"Catalogs synchronized: {report.Inserted} inserted, {report.Updated} updated");
        return 0;
    }

    private static async Task<int> CreatePlatformKeyAsync(
        IServiceProvider services, ImpiFactDbContext db, string name, TextWriter output, CancellationToken ct)
    {
        var generated = services.GetRequiredService<IApiKeyGenerator>().Generate();
        await services.GetRequiredService<IApiKeyStore>().AddAsync(
            new NewApiKey(Guid.CreateVersion7(), CompanyId: null, name, generated.DisplayPrefix, generated.Hash, [ApiScopes.Platform], ExpiresAt: null, CreatedBy: null),
            ct);
        await db.SaveChangesAsync(ct);

        await output.WriteLineAsync("Platform API key created. Copy it now: it is not shown again.");
        await output.WriteLineAsync(generated.PlainText);
        return 0;
    }

    private static ServiceProvider BuildServices(IConfiguration configuration, bool withApplication)
    {
        var services = new ServiceCollection();
        // Todos los motores disponibles; Database:Provider elige uno (sin él, o con uno que no existe, el comando falla
        // con un mensaje que lista los disponibles).
        services.AddInfrastructure(configuration, new PostgresDatabaseProvider());
        services.AddIdentityPersistence();
        if (withApplication)
        {
            // Los casos de uso de Application con sus decoradores. El registro queda sin proveedores: la salida que ve
            // quien corre el comando es solo la del comando.
            services.AddLogging();
            services.AddApplication();
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>
    /// Ambiente como lo resuelve el host genérico de .NET: el valor de <c>DOTNET_ENVIRONMENT</c> o, sin él,
    /// Production. Se compara sin distinguir mayúsculas, como <c>IsDevelopment</c>.
    /// </summary>
    private static bool IsDevelopment(IConfiguration configuration) =>
        string.Equals(EnvironmentName(configuration), "Development", StringComparison.OrdinalIgnoreCase);

    private static string EnvironmentName(IConfiguration configuration) =>
        configuration[EnvironmentVariable] is { Length: > 0 } name ? name : "Production";

    /// <summary>
    /// El Migrator no trae appsettings.json: el proveedor de secretos, que la API toma del suyo, se fija aquí y las
    /// variables de entorno lo pueden cambiar.
    /// </summary>
    private static IConfiguration WithSeedDemoDefaults(IConfiguration configuration) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Secrets:Provider"] = SecretsOptions.LocalAesGcmProvider })
            .AddConfiguration(configuration)
            .Build();

    /// <summary>Contraseña al azar del certificado demo; nunca se muestra ni se guarda en claro.</summary>
    private static string NewCertificatePassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));

    private enum Command
    {
        Migrate,
        CreatePlatformKey,
        SeedDemo,
    }
}
