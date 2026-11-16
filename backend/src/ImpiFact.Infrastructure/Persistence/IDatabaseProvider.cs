using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Infrastructure.Persistence;

/// <summary>
/// Un motor de base de datos (PostgreSQL, SQL Server o MySQL). Cada proyecto <c>ImpiFact.Infrastructure.&lt;Motor&gt;</c>
/// trae uno; <c>AddInfrastructure</c> usa el que nombra <c>Database:Provider</c> en la configuración.
/// </summary>
public interface IDatabaseProvider
{
    /// <summary>Nombre que se escribe en <c>Database:Provider</c>, por ejemplo «Postgres».</summary>
    string Name { get; }

    /// <summary>Registra los servicios propios del motor, como la fábrica de conexiones de lectura.</summary>
    void AddServices(IServiceCollection services);

    /// <summary>Conecta el contexto de EF Core (Entity Framework) con el motor.</summary>
    void ConfigureDbContext(IServiceProvider services, DbContextOptionsBuilder options);
}
