using System.Data.Common;
using ImpiFact.Application.Abstractions;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Infrastructure.Persistence;

/// <summary>
/// Arranque de la API y del Worker solo sobre una base con todas las migraciones aplicadas. Caso real: el Worker
/// arrancó sobre una base sin <c>password_reset_tokens</c> y la tarea diaria Housekeeping falló con «42P01: relation
/// "password_reset_tokens" does not exist». Las migraciones las aplica solo el Migrator; aquí solo se revisan, con
/// <c>GetPendingMigrationsAsync</c> de EF Core, igual en todos los motores.
/// </summary>
public static class PendingMigrationsGuard
{
    /// <summary>Categoría de los registros de esta revisión.</summary>
    public const string LogCategory = "ImpiFact.Startup";

    /// <summary>
    /// Revisa las migraciones y, si no falta ninguna, corre el host hasta que se detenga (como <c>RunAsync</c>). Si
    /// falta alguna, escribe una sola línea crítica con sus nombres, libera el host sin arrancarlo (ningún servicio en
    /// segundo plano llega a correr) y devuelve el código de salida 1.
    /// </summary>
    /// <remarks>
    /// Revisa el esquema, no la disponibilidad: si la base no responde, lo deja como advertencia y arranca como antes,
    /// porque la prueba de vida de la API no depende de la base. Se apaga con
    /// <see cref="DatabaseOptions.CheckPendingMigrationsAtStartup"/>, solo para las pruebas que crean el esquema
    /// después de levantar el host.
    /// </remarks>
    /// <returns>0 cuando el host corrió y se detuvo; 1 cuando no arrancó por migraciones pendientes.</returns>
    public static async Task<int> RunWhenDatabaseIsMigratedAsync(this IHost host, CancellationToken ct = default)
    {
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(LogCategory);
        var options = host.Services.GetRequiredService<IOptionsService<DatabaseOptions>>().Value;
        if (options.CheckPendingMigrationsAtStartup && await FindPendingAsync(host.Services, logger, ct) is [_, ..] pending)
        {
            logger.LogCritical(
                "La base de datos tiene migraciones pendientes: {Migrations}. Corra el Migrator (comando migrate) antes de arrancar: " +
                "en local, «dotnet run --project src/ImpiFact.Migrator -- migrate» desde backend/; en contenedores, " +
                "«docker compose -f deploy/docker-compose.yml --profile app run --rm migrator» o la imagen del Migrator con el comando migrate.",
                string.Join(", ", pending));
            // Liberar el host vacía la cola de los registros antes de que el proceso termine.
            await DisposeAsync(host);
            return 1;
        }

        await host.RunAsync(ct);
        return 0;
    }

    private static async Task<IReadOnlyList<string>> FindPendingAsync(IServiceProvider services, ILogger logger, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(companyId: null, userId: null, ActorType.System);
        var db = scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();
        try
        {
            return [.. await db.Database.GetPendingMigrationsAsync(ct)];
        }
        catch (Exception exception) when (IsUnreachable(exception, scope.ServiceProvider.GetRequiredService<IDatabaseProvider>()))
        {
            logger.LogWarning(
                exception, "No se pudo revisar si la base de datos tiene migraciones pendientes; se arranca sin revisarlo.");
            return [];
        }
    }

    // La falla de conexión llega como DbException transitoria (cada motor dice cuáles lo son) o TimeoutException, a veces
    // envuelta por la estrategia de ejecución. Los demás errores (por ejemplo, una contraseña equivocada) no son pasajeros
    // y detienen el arranque.
    private static bool IsUnreachable(Exception exception, IDatabaseProvider provider) =>
        exception is TimeoutException
        || (exception is DbException database && provider.IsTransient(database))
        || (exception.InnerException is { } inner && IsUnreachable(inner, provider));

    private static async Task DisposeAsync(IHost host)
    {
        if (host is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else
        {
            host.Dispose();
        }
    }
}
