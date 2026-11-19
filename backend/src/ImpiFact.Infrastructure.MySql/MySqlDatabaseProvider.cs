using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Infrastructure.Billing;
using ImpiFact.Infrastructure.Documents;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Platform.Outbox;
using ImpiFact.Infrastructure.Platform.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace ImpiFact.Infrastructure.MySql;

/// <summary>
/// Motor MySQL (8.4 LTS; la versión mínima es la 8.0.16 por los <c>CHECK</c> y <c>LATERAL</c>). EF Core (Entity
/// Framework, con el fork de Pomelo que mantiene Microting: ADR-08) y las lecturas con Dapper comparten un solo
/// <see cref="MySqlDataSource"/>: el origen de datos de MySqlConnector (el controlador .NET de MySQL), que guarda la
/// cadena de conexión y el grupo de conexiones abiertas para reutilizarlas.
/// </summary>
public sealed class MySqlDatabaseProvider : IDatabaseProvider
{
    /// <summary>
    /// Nombre de la fuente de trazas (<c>ActivitySource</c>) de MySqlConnector: emite una actividad por comando sin
    /// paquete de instrumentación aparte, como «Npgsql» en PostgreSQL. La observabilidad la agrega con <c>AddSource</c>.
    /// </summary>
    public const string DriverActivitySourceName = "MySqlConnector";

    /// <summary>
    /// Versión del servidor con que trabaja el proveedor de EF. Se fija en lugar de detectarla para no abrir una conexión
    /// al armar el modelo (las herramientas de EF tampoco tienen base). Es la 8.4, la versión LTS (de soporte largo)
    /// actual de MySQL; la 8.0 dejó de tener soporte en abril de 2026.
    /// </summary>
    internal static readonly ServerVersion ServerVersion = new MySqlServerVersion(new Version(8, 4, 0));

    public string Name => "MySql";

    /// <summary>Fuentes de trazas del controlador, para conectarlas con la observabilidad compartida.</summary>
    public IReadOnlyList<string> DriverActivitySources => [DriverActivitySourceName];

    public void AddServices(IServiceCollection services)
    {
        // La cadena se lee al primer uso, cuando las opciones ya pasaron la validación del arranque.
        services.TryAddSingleton(serviceProvider =>
            CreateDataSource(serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));
        services.TryAddSingleton<IReadConnectionFactory, MySqlReadConnectionFactory>();
        services.TryAddSingleton<ISqlDialect, MySqlSqlDialect>();
        services.TryAddSingleton<INumberingSql, MySqlNumberingSql>();
        services.TryAddSingleton<IOutboxQueueSql, MySqlOutboxQueueSql>();
        services.TryAddSingleton<IScheduledJobLockSql, MySqlScheduledJobLockSql>();
        services.TryAddSingleton<IPlanUsageSql, MySqlPlanUsageSql>();
    }

    /// <summary>
    /// Cada contexto recibe una conexión nueva del origen de datos (que el contexto abre y cierra; al cerrarla vuelve al
    /// grupo), en lugar del origen mismo: el proveedor de EF arma sus servicios internos por cada origen de datos
    /// distinto, y con un host por clase de prueba EF pasaría de veinte y fallaría con
    /// <c>ManyServiceProvidersCreatedWarning</c>, también en los contextos de otros motores del mismo proceso.
    /// </summary>
    public void ConfigureDbContext(IServiceProvider services, DbContextOptionsBuilder options) =>
        Configure(options.UseMySql(
            services.GetRequiredService<MySqlDataSource>().CreateConnection(), ServerVersion, UseThisAssemblyForMigrations));

    // 1062 es el código de MySQL para «ER_DUP_ENTRY» (llave única repetida).
    public bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry };

    /// <summary>
    /// Origen de datos con los ajustes que el resto del código da por hechos, sea cual sea la cadena de conexión:
    /// <list type="bullet">
    /// <item>Los uuid son <c>char(36)</c> (texto con guiones) y el controlador los lee como <see cref="Guid"/>.</item>
    /// <item>Las fechas <c>datetime(6)</c> se guardan en UTC y se leen como <see cref="DateTime"/> en UTC.</item>
    /// <item>
    /// Cada sesión usa <c>READ COMMITTED</c> como PostgreSQL (Modelo de datos detallado, sección 8, punto 8): sin eso
    /// MySQL usa <c>REPEATABLE READ</c>, que bloquea huecos del índice y deja leer datos viejos dentro de una
    /// transacción. Se fija al abrir una conexión nueva o reiniciada (al volver al grupo, MySQL borra las variables de la
    /// sesión).
    /// </item>
    /// </list>
    /// </summary>
    internal static MySqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new MySqlDataSourceBuilder(connectionString);
        // El proveedor de EF las exige: usa variables de sesión (@var) en su SQL y cuenta las filas encontradas.
        builder.ConnectionStringBuilder.AllowUserVariables = true;
        builder.ConnectionStringBuilder.UseAffectedRows = false;
        builder.ConnectionStringBuilder.GuidFormat = MySqlGuidFormat.Char36;
        builder.ConnectionStringBuilder.DateTimeKind = MySqlDateTimeKind.Utc;
        builder.UseConnectionOpenedCallback(SetSessionDefaultsAsync);
        return builder.Build();
    }

    /// <summary>
    /// Ajustes comunes a la aplicación y a las herramientas de EF (ver <see cref="DesignTimeDbContextFactory"/>): las
    /// migraciones viven en este proyecto y los tipos propios de MySQL (como json) los fija el personalizador.
    /// </summary>
    internal static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options) =>
        options.ReplaceService<IModelCustomizer, MySqlModelCustomizer>();

    internal static void UseThisAssemblyForMigrations(MySqlDbContextOptionsBuilder mysql) =>
        mysql.MigrationsAssembly(typeof(MySqlDatabaseProvider).Assembly.GetName().Name);

    private static async ValueTask SetSessionDefaultsAsync(MySqlConnectionOpenedContext context, CancellationToken ct)
    {
        if (context.Conditions == MySqlConnectionOpenedConditions.None)
        {
            // Conexión del grupo sin reiniciar: la sesión conserva lo que se fijó al abrirla.
            return;
        }

        await using var command = context.Connection.CreateCommand();
        command.CommandText = "SET SESSION TRANSACTION ISOLATION LEVEL READ COMMITTED";
        await command.ExecuteNonQueryAsync(ct);
    }
}
