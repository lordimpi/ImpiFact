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
/// vez y en la base queda solo su resumen SHA-256.
/// </summary>
/// <remarks>
/// Códigos de salida: 0 si todo salió bien, 1 si falta configuración o falló la base, 2 si el comando no existe.
/// Corre como actor de sistema (<see cref="ActorType.System"/>), que puede escribir datos de cualquier empresa.
/// </remarks>
public static class MigratorApp
{
    private const string Usage = "Usage: ImpiFact.Migrator migrate | ImpiFact.Migrator create-platform-key <name>";

    public static async Task<int> RunAsync(string[] args, IConfiguration configuration, TextWriter output, TextWriter error, CancellationToken ct)
    {
        var command = args switch
        {
            ["migrate"] => Command.Migrate,
            ["create-platform-key", var name] when !string.IsNullOrWhiteSpace(name) => Command.CreatePlatformKey,
            _ => (Command?)null,
        };
        if (command is null)
        {
            await error.WriteLineAsync(Usage);
            return 2;
        }

        await using var services = BuildServices(configuration);
        try
        {
            // Valida la configuración de la base antes de tocarla: así el error nombra el dato que falta.
            _ = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
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

    private static ServiceProvider BuildServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration, new PostgresDatabaseProvider());
        services.AddIdentityPersistence();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private enum Command
    {
        Migrate,
        CreatePlatformKey,
    }
}
