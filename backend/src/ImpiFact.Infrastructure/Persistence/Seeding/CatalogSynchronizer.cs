using System.Reflection;
using ImpiFact.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Infrastructure.Persistence.Seeding;

/// <summary>Cuántas filas de catálogo se agregaron y cuántas se corrigieron en una sincronización.</summary>
public sealed record CatalogSyncReport(int Inserted, int Updated);

/// <summary>
/// Lleva los catálogos DIAN de los JSON de semilla (<c>Seed/*.json</c>) a la base. Lo ejecuta ImpiFact.Migrator
/// después de aplicar las migraciones, en cada despliegue. Agrega las filas que faltan y corrige las que cambiaron;
/// nunca borra, porque documentos ya emitidos pueden apuntar a una fila vieja.
/// </summary>
/// <remarks>
/// Es idempotente: una segunda ejecución sobre la misma base no cambia nada. No toca <c>is_active</c>,
/// <c>valid_from</c> ni <c>valid_to</c>: el PHP no los tiene y los administra ImpiFact. Debe correr con un actor
/// de sistema (<see cref="Application.Abstractions.ActorType.System"/>), como el resto del Migrator.
/// </remarks>
public sealed class CatalogSynchronizer(ImpiFactDbContext db)
{
    private static readonly MethodInfo SynchronizeTableMethod =
        typeof(CatalogSynchronizer).GetMethod(nameof(SynchronizeTableAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly HashSet<string> ManagedByImpiFact =
        [nameof(CatalogEntry.IsActive), nameof(CatalogEntry.ValidFrom), nameof(CatalogEntry.ValidTo)];

    public async Task<CatalogSyncReport> SynchronizeAsync(CancellationToken ct)
    {
        var inserted = 0;
        var updated = 0;
        var catalogs = db.Model.GetEntityTypes()
            .Where(type => type.ClrType.IsSubclassOf(typeof(CatalogEntry)))
            .Select(type => (type.ClrType, Table: type.GetTableName()!));

        foreach (var (clrType, table) in catalogs)
        {
            var task = (Task<(int, int)>)SynchronizeTableMethod.MakeGenericMethod(clrType).Invoke(this, [table, ct])!;
            var (added, changed) = await task;
            inserted += added;
            updated += changed;
        }

        // Un solo guardado: EF ordena los INSERT por sus llaves foráneas (países, luego departamentos, luego municipios).
        await db.SaveChangesAsync(ct);
        return new CatalogSyncReport(inserted, updated);
    }

    private async Task<(int Inserted, int Updated)> SynchronizeTableAsync<T>(string table, CancellationToken ct)
        where T : CatalogEntry
    {
        var seed = CatalogSeed.Load<T>(table);
        var existing = await db.Set<T>().ToDictionaryAsync(entry => entry.Id, ct);
        var properties = db.Model.FindEntityType(typeof(T))!.GetProperties()
            .Where(property => !property.IsPrimaryKey() && !ManagedByImpiFact.Contains(property.Name) && property.PropertyInfo is not null)
            .ToList();

        var inserted = 0;
        var updated = 0;
        foreach (var row in seed)
        {
            if (!existing.TryGetValue(row.Id, out var current))
            {
                db.Set<T>().Add(row);
                inserted++;
                continue;
            }

            var entry = db.Entry(current);
            var changed = false;
            foreach (var property in properties)
            {
                var value = property.PropertyInfo!.GetValue(row);
                if (!Equals(entry.Property(property.Name).CurrentValue, value))
                {
                    entry.Property(property.Name).CurrentValue = value;
                    changed = true;
                }
            }

            updated += changed ? 1 : 0;
        }

        return (inserted, updated);
    }
}
