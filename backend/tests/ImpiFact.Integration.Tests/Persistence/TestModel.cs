using ImpiFact.Application.Abstractions;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Integration.Tests.Persistence;

/// <summary>
/// Entidad de prueba que reúne todas las marcas: pertenece a una empresa, guarda fechas de creación y
/// cambio, lleva versión de fila y admite borrado lógico. Solo existe en las pruebas; el modelo real llega
/// en la tarea 7.
/// </summary>
public sealed class Widget : ITenantOwned, IAuditable, IVersioned, ISoftDeletable
{
    public Guid Id { get; set; }

    public Guid CompanyId { get; set; }

    public Guid? GroupId { get; set; }

    public string DisplayName { get; set; } = "";

    /// <summary>Sin precisión propia: toma la de la convención, numeric(18,2).</summary>
    public decimal Price { get; set; }

    /// <summary>Con precisión propia, numeric(18,6), como los precios unitarios del modelo.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Se guarda como JSON en una sola columna.</summary>
    public WidgetDetails Details { get; set; } = new();

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public long RowVersion { get; private set; }

    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>Valor JSON mutable: sirve para ver que EF detecta un cambio hecho dentro del objeto.</summary>
public sealed class WidgetDetails
{
    public List<string> Tags { get; set; } = [];

    public string Note { get; set; } = "";
}

/// <summary>Segunda entidad de prueba, para ver el nombre de la llave foránea entre dos tablas.</summary>
public sealed class WidgetGroup : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid CompanyId { get; set; }

    public string GroupName { get; set; } = "";
}

/// <summary>Contexto de prueba: el de producción con dos entidades de prueba en lugar del modelo real.</summary>
public sealed class TestDbContext(DbContextOptions<TestDbContext> options, IClock clock, ITenantContext tenant)
    : ImpiFactDbContext(options, clock, tenant)
{
    public DbSet<Widget> Widgets => Set<Widget>();

    public DbSet<WidgetGroup> WidgetGroups => Set<WidgetGroup>();

    protected override void ConfigureEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WidgetGroup>(group => group.Property(g => g.GroupName).HasMaxLength(50));

        modelBuilder.Entity<Widget>(widget =>
        {
            widget.Property(w => w.DisplayName).HasMaxLength(100);
            widget.Property(w => w.UnitPrice).HasPrecision(18, 6);
            widget.Property(w => w.Details).HasJsonConversion();
            widget.HasIndex(w => new { w.CompanyId, w.DisplayName }).IsUnique();
            widget.HasIndex(w => w.OccurredAt);
            widget.HasOne<WidgetGroup>().WithMany().HasForeignKey(w => w.GroupId);
        });
    }
}

/// <summary>Reloj fijo que la prueba adelanta a mano.</summary>
public sealed class FixedClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = start;

    public void Advance(TimeSpan by) => UtcNow += by;
}
