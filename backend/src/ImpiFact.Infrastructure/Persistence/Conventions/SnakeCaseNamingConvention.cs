using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace ImpiFact.Infrastructure.Persistence.Conventions;

/// <summary>
/// Convención global de nombres (Modelo de datos detallado, sección 2): tablas y columnas en snake_case
/// (minúsculas separadas por guion bajo) y llaves e índices con prefijo: <c>pk_&lt;tabla&gt;</c> para la llave
/// primaria, <c>ux_&lt;tabla&gt;_&lt;columnas&gt;</c> para índices y llaves únicas, <c>ix_&lt;tabla&gt;_&lt;columnas&gt;</c>
/// para los demás índices y <c>fk_&lt;hija&gt;_&lt;padre&gt;</c> para las llaves foráneas (referencias a otra tabla).
/// </summary>
/// <remarks>
/// Corre al cerrar el modelo, después de todas las configuraciones, y escribe con la prioridad más baja de EF
/// (la de convención): un nombre puesto a mano en una configuración, por ejemplo con <c>ToTable("taxes")</c>,
/// se respeta. Todo nombre se acorta a 63 caracteres con <see cref="DbIdentifier.Shorten"/>.
/// </remarks>
internal sealed class SnakeCaseNamingConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        var entityTypes = modelBuilder.Metadata.GetEntityTypes().ToList();

        // Primero las tablas: los nombres de llaves e índices de la segunda vuelta usan los nombres finales.
        foreach (var entityType in entityTypes.Where(type => type.BaseType is null && !type.IsOwned()))
        {
            if (entityType.GetTableName() is { } table)
            {
                entityType.Builder.ToTable(Name(DbIdentifier.ToSnakeCase(table)));
            }
        }

        foreach (var entityType in entityTypes)
        {
            if (entityType.GetTableName() is not { } table)
            {
                continue;
            }

            foreach (var property in entityType.GetDeclaredProperties())
            {
                property.Builder.HasColumnName(Name(DbIdentifier.ToSnakeCase(property.GetColumnName())));
            }

            foreach (var key in entityType.GetDeclaredKeys())
            {
                key.Builder.HasName(Name(key.IsPrimaryKey() ? $"pk_{table}" : $"ux_{table}_{Columns(key.Properties)}"));
            }

            foreach (var index in entityType.GetDeclaredIndexes())
            {
                var prefix = index.IsUnique ? "ux" : "ix";
                index.Builder.HasDatabaseName(Name($"{prefix}_{table}_{Columns(index.Properties)}"));
            }

            NameForeignKeys(entityType, table);
        }
    }

    /// <summary>
    /// <c>fk_&lt;hija&gt;_&lt;padre&gt;</c>; si la tabla tiene dos referencias a la misma tabla padre (por ejemplo una
    /// nota que apunta a otro documento), se agregan las columnas para que los nombres no choquen.
    /// </summary>
    private static void NameForeignKeys(IConventionEntityType entityType, string table)
    {
        var foreignKeys = entityType.GetDeclaredForeignKeys()
            .Select(foreignKey => (ForeignKey: foreignKey, Parent: foreignKey.PrincipalEntityType.GetTableName()))
            .Where(item => item.Parent is not null)
            .ToList();
        var repeatedParents = foreignKeys.GroupBy(item => item.Parent).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();

        foreach (var (foreignKey, parent) in foreignKeys)
        {
            var name = repeatedParents.Contains(parent) ? $"fk_{table}_{parent}_{Columns(foreignKey.Properties)}" : $"fk_{table}_{parent}";
            foreignKey.Builder.HasConstraintName(Name(name));
        }
    }

    private static string Columns(IEnumerable<IReadOnlyProperty> properties) =>
        string.Join("_", properties.Select(property => property.GetColumnName()));

    private static string Name(string name) => DbIdentifier.Shorten(name);
}
