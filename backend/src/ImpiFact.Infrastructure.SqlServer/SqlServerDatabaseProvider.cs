using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Infrastructure.Billing;
using ImpiFact.Infrastructure.Documents;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Platform.Outbox;
using ImpiFact.Infrastructure.Platform.Scheduling;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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
    /// Ajustes comunes a la aplicación y a las herramientas de EF (ver <see cref="DesignTimeDbContextFactory"/>): las
    /// migraciones viven en este proyecto y los tipos y filtros propios de SQL Server los fija el personalizador.
    /// </summary>
    internal static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options) =>
        options.ReplaceService<IModelCustomizer, SqlServerModelCustomizer>();

    internal static void UseThisAssemblyForMigrations(Microsoft.EntityFrameworkCore.Infrastructure.SqlServerDbContextOptionsBuilder sqlServer) =>
        sqlServer.MigrationsAssembly(typeof(SqlServerDatabaseProvider).Assembly.GetName().Name);
}
