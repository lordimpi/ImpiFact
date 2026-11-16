using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ImpiFact.Infrastructure.Postgres;

/// <summary>
/// Motor PostgreSQL. EF Core (Entity Framework) y las lecturas con Dapper comparten un solo
/// <see cref="NpgsqlDataSource"/>: el origen de datos de Npgsql (el controlador .NET de PostgreSQL), que guarda la
/// cadena de conexión y el grupo de conexiones abiertas para reutilizarlas.
/// </summary>
public sealed class PostgresDatabaseProvider : IDatabaseProvider
{
    public string Name => "Postgres";

    public void AddServices(IServiceCollection services)
    {
        // La cadena se lee al primer uso, cuando las opciones ya pasaron la validación del arranque.
        services.TryAddSingleton(serviceProvider =>
            NpgsqlDataSource.Create(serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));
        services.TryAddSingleton<IReadConnectionFactory, NpgsqlReadConnectionFactory>();
    }

    public void ConfigureDbContext(IServiceProvider services, DbContextOptionsBuilder options) =>
        Configure(options.UseNpgsql(services.GetRequiredService<NpgsqlDataSource>(), UseThisAssemblyForMigrations));

    /// <summary>
    /// Ajustes comunes a la aplicación y a las herramientas de EF (ver <see cref="DesignTimeDbContextFactory"/>): las
    /// migraciones viven en este proyecto y los tipos propios de PostgreSQL (como jsonb) los fija el personalizador.
    /// </summary>
    // 23505 es el código de PostgreSQL para «unique_violation».
    public bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    internal static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options) =>
        options.ReplaceService<IModelCustomizer, PostgresModelCustomizer>();

    internal static void UseThisAssemblyForMigrations(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsAssembly(typeof(PostgresDatabaseProvider).Assembly.GetName().Name);
}
