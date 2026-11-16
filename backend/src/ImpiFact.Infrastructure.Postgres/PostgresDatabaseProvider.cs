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
        options.UseNpgsql(services.GetRequiredService<NpgsqlDataSource>())
            .ReplaceService<IModelCustomizer, PostgresModelCustomizer>();
}
