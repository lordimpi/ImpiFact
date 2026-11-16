using System.Reflection;
using ImpiFact.Application.Abstractions;
using ImpiFact.Domain.Common;
using ImpiFact.Infrastructure.Persistence.Conventions;
using ImpiFact.Infrastructure.Persistence.DataProtection;
using ImpiFact.Infrastructure.Persistence.Conversions;
using ImpiFact.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Infrastructure.Persistence;

/// <summary>
/// Contexto único de EF Core (Entity Framework, el mapeador de objetos a tablas) para las escrituras de todos
/// los módulos (Arquitectura detallada, sección 4.4; Modelo de datos detallado, secciones 2 y 7). Aplica a todo
/// el modelo las reglas comunes: nombres en snake_case, decimales con precisión explícita, instantes en UTC,
/// llaves uuid v7 generadas por la aplicación, fechas de auditoría y versión de fila, y los filtros globales de
/// empresa y de borrado lógico.
/// </summary>
/// <remarks>
/// Las reglas están en métodos sellados (que una clase hija no puede cambiar); una hija solo cambia qué entidades
/// hay, con <see cref="ConfigureEntities"/>. Así el contexto de las pruebas comprueba exactamente las mismas reglas.
/// La tabla de llaves de Data Protection (<see cref="DataProtectionKeys"/>) está siempre, también en esos contextos.
/// </remarks>
public class ImpiFactDbContext : DbContext, IDataProtectionKeyContext
{
    private static readonly MethodInfo ApplyTenantFilterMethod =
        typeof(ImpiFactDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo ApplySoftDeleteFilterMethod =
        typeof(ImpiFactDbContext).GetMethod(nameof(ApplySoftDeleteFilter), BindingFlags.Static | BindingFlags.NonPublic)!;

    private readonly IClock clock;
    private readonly ITenantContext tenant;

    public ImpiFactDbContext(DbContextOptions<ImpiFactDbContext> options, IClock clock, ITenantContext tenant)
        : this((DbContextOptions)options, clock, tenant)
    {
    }

    /// <summary>Para contextos hijos, que pasan sus propias opciones.</summary>
    protected ImpiFactDbContext(DbContextOptions options, IClock clock, ITenantContext tenant)
        : base(options)
    {
        this.clock = clock;
        this.tenant = tenant;
    }

    /// <summary>
    /// Empresa de la solicitud. El filtro de tenant la lee desde aquí, y EF la vuelve a leer en cada consulta, así
    /// cada contexto filtra por su propia empresa aunque el modelo se construya una sola vez.
    /// </summary>
    private Guid? CurrentCompanyId => tenant.CompanyId;

    /// <summary>
    /// Llaves de ASP.NET Core Data Protection, compartidas por todas las instancias (ADR-25). Solo las lee y
    /// escribe Data Protection; ningún caso de uso las toca.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected sealed override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.AddInterceptors(new AuditInterceptor(clock));

    protected sealed override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Precisión por defecto de los importes; cantidades, precios unitarios y tarifas la fijan en su configuración.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<UtcDateTimeOffsetConverter>();
        configurationBuilder.Conventions.Add(_ => new SnakeCaseNamingConvention());
    }

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DataProtectionKey>(DataProtectionKeyMapping.Configure);
        ConfigureEntities(modelBuilder);

        // Solo se toca modelBuilder.Entity cuando hay una regla que aplicar: llamarlo marca el tipo como entidad
        // explícita, y EF dejaría como tabla un tipo que solo descubrió de paso (por ejemplo, el de una columna JSON).
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(type => type.BaseType is null && !type.IsOwned()).ToList())
        {
            var clrType = entityType.ClrType;

            if (entityType.FindPrimaryKey()?.Properties is [{ ClrType: var keyType, Name: var keyName }] && keyType == typeof(Guid))
            {
                modelBuilder.Entity(clrType).Property(keyName).HasValueGenerator<UuidV7ValueGenerator>().ValueGeneratedOnAdd();
            }

            if (typeof(IVersioned).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType).Property(nameof(IVersioned.RowVersion)).IsConcurrencyToken();
            }

            if (typeof(ITenantOwned).IsAssignableFrom(clrType))
            {
                ApplyTenantFilterMethod.MakeGenericMethod(clrType).Invoke(this, [modelBuilder]);
            }

            if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
            {
                ApplySoftDeleteFilterMethod.MakeGenericMethod(clrType).Invoke(null, [modelBuilder]);
            }
        }
    }

    /// <summary>Registra las entidades del modelo. Por defecto, todas las configuraciones de este ensamblado.</summary>
    protected virtual void ConfigureEntities(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ImpiFactDbContext).Assembly);

    /// <summary>
    /// Una consulta solo ve las filas de la empresa de la solicitud. Sin empresa (por ejemplo, antes de iniciar
    /// sesión) no ve ninguna: falla cerrado. Los procesos de plataforma que necesitan ver varias empresas lo piden
    /// a propósito con <c>IgnoreQueryFilters([QueryFilters.Tenant])</c>.
    /// </summary>
    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantOwned =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(QueryFilters.Tenant, entity => entity.CompanyId == CurrentCompanyId);

    private static void ApplySoftDeleteFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ISoftDeletable =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(QueryFilters.SoftDelete, entity => entity.DeletedAt == null);
}
