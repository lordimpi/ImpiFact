using ImpiFact.Application.Abstractions;
using ImpiFact.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ImpiFact.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Interceptor de guardado (código que EF ejecuta justo antes de escribir los cambios; Arquitectura detallada,
/// sección 4.4). En las filas nuevas pone <c>created_at</c> y <c>updated_at</c> con el reloj de la aplicación y
/// <c>row_version</c> en 1. En las modificadas mueve <c>updated_at</c>, impide cambiar <c>created_at</c> y sube
/// <c>row_version</c> en uno. Como <c>row_version</c> es token de concurrencia, EF agrega al UPDATE la condición
/// «row_version = versión leída»: si otro guardó antes, no se actualiza ninguna fila y EF lanza
/// <see cref="DbUpdateConcurrencyException"/>. Es igual en los tres motores, sin depender de un tipo propio de cada uno.
/// </summary>
internal sealed class AuditInterceptor(IClock clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.UtcNow;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                StampTimestamps(entry, now);
                StampRowVersion(entry);
            }
        }
    }

    private static void StampTimestamps(EntityEntry entry, DateTimeOffset now)
    {
        if (entry.Entity is not IAuditable)
        {
            return;
        }

        var createdAt = entry.Property(nameof(IAuditable.CreatedAt));
        if (entry.State == EntityState.Added)
        {
            createdAt.CurrentValue = now;
        }
        else
        {
            createdAt.IsModified = false;
        }

        entry.Property(nameof(IAuditable.UpdatedAt)).CurrentValue = now;
    }

    private static void StampRowVersion(EntityEntry entry)
    {
        if (entry.Entity is not IVersioned)
        {
            return;
        }

        var rowVersion = entry.Property(nameof(IVersioned.RowVersion));
        rowVersion.CurrentValue = entry.State == EntityState.Added ? 1L : (long)rowVersion.OriginalValue! + 1;
    }
}
