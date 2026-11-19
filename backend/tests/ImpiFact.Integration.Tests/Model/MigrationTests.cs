using ImpiFact.Domain.Catalog;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Persistence.Seeding;
using ImpiFact.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ImpiFact.Integration.Tests.Model;

/// <summary>
/// Las migraciones y la sincronización de catálogos (Modelo de datos detallado, sección 9). La base de estas
/// pruebas se crea con las migraciones reales, así las pruebas de esquema y de aislamiento revisan lo mismo que
/// queda en un despliegue.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class MigrationTests
{
    // Si alguien cambia una entidad o configuración sin crear la migración, la instantánea queda vieja y esto falla.
    [Test]
    public void The_model_has_no_changes_missing_from_the_migrations()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);

        Assert.That(db.Database.HasPendingModelChanges(), Is.False, $"Run dotnet ef migrations add in ImpiFact.Infrastructure.{TestDatabase.ProviderName}.");
    }

    // Las configuraciones comunes declaran los índices parciales sin SQL; el personalizador del motor escribe cada filtro
    // y quita la marca, así ninguna queda en el modelo ni en la instantánea.
    [Test]
    public void The_provider_writes_every_portable_index_filter()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var indexes = db.GetService<IDesignTimeModel>().Model.GetEntityTypes().SelectMany(entityType => entityType.GetDeclaredIndexes()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(indexes.Where(index => index.FindAnnotation(PortableIndexFilters.AnnotationName) is not null).Select(index => index.GetDatabaseName()), Is.Empty);
            Assert.That(
                indexes.Where(index => index.GetFilter() is not null).ToDictionary(index => index.GetDatabaseName()!, index => index.GetFilter()),
                Is.EqualTo(new Dictionary<string, string?>
                {
                    ["ux_certificates_active"] = "is_active",
                    ["ux_documents_cufe"] = "cufe IS NOT NULL",
                    ["ix_documents_track"] = "track_id IS NOT NULL",
                }));
        });
    }

    [Test]
    public async Task Every_migration_is_applied_to_the_test_database()
    {
        await using var session = ModelDatabase.Open(companyId: null);

        var pending = await session.Db.Database.GetPendingMigrationsAsync();
        var applied = await session.Db.Database.GetAppliedMigrationsAsync();

        Assert.That(pending, Is.Empty);
        Assert.That(applied, Is.Not.Empty);
    }

    [Test]
    public async Task A_second_synchronization_changes_nothing()
    {
        await using var session = ModelDatabase.Open(companyId: null);

        var report = await new CatalogSynchronizer(session.Db).SynchronizeAsync(CancellationToken.None);

        Assert.That(report, Is.EqualTo(new CatalogSyncReport(Inserted: 0, Updated: 0)));
    }

    [Test]
    public async Task Synchronization_restores_a_changed_catalog_row_without_touching_is_active()
    {
        await using (var session = ModelDatabase.Open(companyId: null))
        {
            // Con parámetros: cada motor escribe el booleano a su modo (SQL Server no tiene true ni false).
            await session.Db.Database.ExecuteSqlAsync(
                $"UPDATE type_environments SET name = {"Cambiado a mano"}, is_active = {false} WHERE id = 2");
        }

        CatalogSyncReport report;
        await using (var session = ModelDatabase.Open(companyId: null))
        {
            report = await new CatalogSynchronizer(session.Db).SynchronizeAsync(CancellationToken.None);
        }

        await using var check = ModelDatabase.Open(companyId: null);
        var row = await check.Db.Set<TypeEnvironment>().AsNoTracking().SingleAsync(entry => entry.Id == 2);
        await check.Db.Database.ExecuteSqlAsync($"UPDATE type_environments SET is_active = {true} WHERE id = 2");

        Assert.That(report, Is.EqualTo(new CatalogSyncReport(Inserted: 0, Updated: 1)));
        Assert.That(row.Name, Is.EqualTo("Pruebas"));
        Assert.That(row.IsActive, Is.False, "is_active is managed by ImpiFact, not by the PHP seed.");
    }
}
