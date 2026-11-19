using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Infrastructure.Billing;
using ImpiFact.Infrastructure.Documents;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Platform.Outbox;
using ImpiFact.Infrastructure.Platform.Scheduling;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace ImpiFact.Infrastructure.SqlServer;

/// <summary>
/// Motor SQL Server (2019 o posterior, también Azure SQL). EF Core (Entity Framework) y las lecturas con Dapper usan
/// la misma cadena de conexión, y por eso el mismo grupo de conexiones de SqlClient (el controlador .NET de SQL
/// Server), que agrupa las conexiones abiertas por cadena.
/// </summary>
public sealed class SqlServerDatabaseProvider : IDatabaseProvider
{
    /// <summary>Errores de SQL Server por una llave repetida: 2627 en una llave única o primaria, 2601 en un índice único.</summary>
    private static readonly HashSet<int> UniqueViolationNumbers = [2627, 2601];

    /// <summary>
    /// Errores pasajeros de SQL Server y Azure SQL. Al conectar: <c>-2</c> tiempo agotado, <c>2</c> y <c>53</c> servidor no
    /// encontrado, <c>258</c> espera agotada, <c>10060</c> sin respuesta, <c>10061</c> conexión rechazada, <c>11001</c>
    /// nombre que no resuelve, <c>64</c>, <c>121</c>, <c>233</c>, <c>10053</c> y <c>10054</c> conexión cortada, <c>20</c>
    /// instancia sin servicio, <c>4060</c> base que no abre. Con el servidor arriba: <c>1205</c> víctima de un interbloqueo
    /// y los de Azure SQL por carga o conmutación (<c>10928</c>, <c>10929</c>, <c>40197</c>, <c>40501</c>, <c>40613</c>,
    /// <c>49918</c>, <c>49919</c>, <c>49920</c>), los mismos que reintenta EF Core.
    /// </summary>
    private static readonly HashSet<int> TransientNumbers =
    [
        -2, 2, 20, 53, 64, 121, 233, 258, 4060, 10053, 10054, 10060, 10061, 11001,
        1205, 10928, 10929, 40197, 40501, 40613, 49918, 49919, 49920,
    ];

    public string Name => "SqlServer";

    public void AddServices(IServiceCollection services)
    {
        services.TryAddSingleton<IReadConnectionFactory, SqlServerReadConnectionFactory>();
        services.TryAddSingleton<ISqlDialect, SqlServerSqlDialect>();
        services.TryAddSingleton<INumberingSql, SqlServerNumberingSql>();
        services.TryAddSingleton<IOutboxQueueSql, SqlServerOutboxQueueSql>();
        services.TryAddSingleton<IScheduledJobLockSql, SqlServerScheduledJobLockSql>();
        services.TryAddSingleton<IPlanUsageSql, SqlServerPlanUsageSql>();
    }

    // La cadena se lee al primer uso, cuando las opciones ya pasaron la validación del arranque.
    public void ConfigureDbContext(IServiceProvider services, DbContextOptionsBuilder options) =>
        Configure(options.UseSqlServer(
            services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString, UseThisAssemblyForMigrations));

    public bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sql && UniqueViolationNumbers.Contains(sql.Number);

    /// <summary>
    /// SqlClient no marca <c>IsTransient</c>: se decide por el número de error (ver <see cref="TransientNumbers"/>). Una
    /// contraseña equivocada (<c>18456</c>) o una restricción violada no son pasajeras.
    /// </summary>
    public bool IsTransient(DbException exception) => exception is SqlException sql && TransientNumbers.Contains(sql.Number);

    /// <summary>
    /// SqlClient no tiene una fuente de trazas propia (como la de Npgsql): la instrumentación de OpenTelemetry (paquete
    /// estable) escucha sus eventos de diagnóstico y crea un tramo por comando, sin los valores de los parámetros.
    /// </summary>
    public void ConfigureTracing(TracerProviderBuilder tracing) => tracing.AddSqlClientInstrumentation();

    /// <summary>La misma instrumentación mide la duración de cada comando (<c>db.client.operation.duration</c>).</summary>
    public void ConfigureMetrics(MeterProviderBuilder metrics) => metrics.AddSqlClientInstrumentation();

    /// <summary>
    /// Ajustes comunes a la aplicación y a las herramientas de EF (ver <see cref="DesignTimeDbContextFactory"/>): las
    /// migraciones viven en este proyecto y los tipos y filtros propios de SQL Server los fija el personalizador.
    /// </summary>
    internal static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options) =>
        options.ReplaceService<IModelCustomizer, SqlServerModelCustomizer>();

    internal static void UseThisAssemblyForMigrations(Microsoft.EntityFrameworkCore.Infrastructure.SqlServerDbContextOptionsBuilder sqlServer) =>
        sqlServer.MigrationsAssembly(typeof(SqlServerDatabaseProvider).Assembly.GetName().Name);
}
