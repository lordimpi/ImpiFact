using ImpiFact.Application.Abstractions;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ImpiFact.Infrastructure.MySql;

/// <summary>
/// Contexto que usan las herramientas de EF Core (Entity Framework, el mapeador de objetos a tablas) para crear
/// migraciones con <c>dotnet ef migrations add</c>. Generar una migración no se conecta a la base: solo compara el
/// modelo con la instantánea anterior, por eso la cadena de conexión es de relleno y la versión del servidor va fija
/// (<see cref="MySqlDatabaseProvider.ServerVersion"/>). Aplicar migraciones lo hace ImpiFact.Migrator, con la
/// configuración real.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ImpiFactDbContext>
{
    public ImpiFactDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ImpiFactDbContext>();
        options.UseMySql(
            "Server=localhost;Database=impifact_design_time", MySqlDatabaseProvider.ServerVersion,
            MySqlDatabaseProvider.UseThisAssemblyForMigrations);
        MySqlDatabaseProvider.Configure(options);

        var tenant = new TenantContext();
        tenant.Set(companyId: null, userId: null, ActorType.System);
        return new ImpiFactDbContext(options.Options, new DesignTimeClock(), tenant);
    }

    private sealed class DesignTimeClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
