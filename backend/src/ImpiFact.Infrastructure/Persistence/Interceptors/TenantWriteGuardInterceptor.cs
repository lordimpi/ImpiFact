using ImpiFact.Application.Abstractions;
using ImpiFact.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ImpiFact.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Guardia de escritura por empresa (tenant: cada empresa cliente con sus datos aislados). El filtro global solo
/// protege las lecturas; este interceptor de guardado revisa cada fila de empresa que se va a insertar, cambiar o
/// borrar y detiene el guardado si no pertenece a la empresa de la solicitud o si alguien intenta moverla a otra.
/// </summary>
/// <remarks>
/// La plataforma (API key sin empresa) y los procesos internos (Worker, Migrator) pueden escribir para cualquier
/// empresa: su autorización se decide antes, en la API. Además, <c>company_id</c> es token de concurrencia, así que
/// el UPDATE o DELETE lleva «company_id = empresa leída»: una fila de otra empresa con el mismo id no se toca y EF
/// lanza <see cref="DbUpdateConcurrencyException"/>. Una violación es un error de programación o un intento de
/// abuso, no un flujo esperado; por eso es una excepción y no un resultado fallido.
/// </remarks>
internal sealed class TenantWriteGuardInterceptor(ITenantContext tenant) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Check(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Check(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Check(DbContext? context)
    {
        if (context is null || tenant.Actor is ActorType.Platform or ActorType.System)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            var original = entry.State == EntityState.Added
                ? entry.Entity.CompanyId
                : (Guid)entry.Property(nameof(ITenantOwned.CompanyId)).OriginalValue!;
            if (tenant.CompanyId is not { } company || entry.Entity.CompanyId != company || original != company)
            {
                throw new InvalidOperationException(
                    $"{entry.Metadata.ClrType.Name} ({entry.State}) does not belong to the company of the current request.");
            }
        }
    }
}
