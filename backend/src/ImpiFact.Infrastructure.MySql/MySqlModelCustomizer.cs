using System.Globalization;
using System.Text.Json;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ImpiFact.Infrastructure.MySql;

/// <summary>
/// Ajustes del modelo propios de MySQL, después de las configuraciones comunes (Modelo de datos detallado, sección 8):
/// <list type="bullet">
/// <item>
/// Texto en <c>utf8mb4</c> (todo Unicode, incluidos los emoji) con la intercalación <see cref="Collation"/>: distingue
/// mayúsculas y tildes, como PostgreSQL, así un índice único o un <c>=</c> se comportan igual en los dos motores. Las
/// búsquedas sin mayúsculas las pide el dialecto con <c>collate utf8mb4_0900_as_ci</c>, donde PostgreSQL usa <c>ilike</c>.
/// </item>
/// <item>Columnas JSON con el tipo <c>json</c>, que MySQL valida al escribir.</item>
/// <item>
/// Los uuid quedan como los deja el proveedor: <c>char(36)</c> en <c>ascii</c>, el texto con guiones en minúscula.
/// Ordena igual que el <c>uuid</c> de PostgreSQL (la paginación por clave compara ids) y se lee a simple vista.
/// </item>
/// <item>Índices parciales, que MySQL no tiene: ver <see cref="ApplyFilter"/>.</item>
/// </list>
/// </summary>
internal sealed class MySqlModelCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    /// <summary>Intercalación de la base, las tablas y las columnas de texto.</summary>
    internal const string Collation = "utf8mb4_0900_as_cs";

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        modelBuilder.HasCharSet("utf8mb4");
        modelBuilder.UseCollation(Collation);

        var jsonProperties = modelBuilder.Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetDeclaredProperties())
            .Where(property => JsonColumn.IsJsonColumn(property) && property.GetColumnType() is null);
        foreach (var property in jsonProperties)
        {
            property.SetColumnType("json");
        }

        PortableIndexFilters.Apply(modelBuilder, (index, filter) => ApplyFilter(modelBuilder, index, filter));
    }

    /// <summary>
    /// MySQL no tiene índices parciales (con <c>WHERE</c>); cada filtro conserva su garantía de otra forma:
    /// <list type="bullet">
    /// <item>
    /// <see cref="PortableIndexFilterKind.WhereNotNull"/>: índice normal. Un índice único de MySQL acepta varias filas con
    /// NULL (no los considera iguales), así <c>ux_documents_cufe</c> sigue permitiendo varios documentos sin CUFE; uno no
    /// único, como <c>ix_documents_track</c>, solo guarda también las filas con NULL.
    /// </item>
    /// <item>
    /// <see cref="PortableIndexFilterKind.WhereTrue"/> en un índice único: una columna generada (calculada por MySQL, que
    /// no se guarda en disco) con el valor de la columna del índice cuando el filtro es verdadero y NULL si no, y el
    /// índice único sobre ella. Por ejemplo <c>active_company_id</c> = <c>company_id</c> si <c>is_active</c>: solo un
    /// certificado activo por empresa y tantos inactivos como haga falta.
    /// </item>
    /// </list>
    /// </summary>
    private static void ApplyFilter(ModelBuilder modelBuilder, IMutableIndex index, PortableIndexFilter filter)
    {
        switch (filter.Kind)
        {
            case PortableIndexFilterKind.WhereNotNull:
                return;
            case PortableIndexFilterKind.WhereTrue when index.IsUnique && index.Properties.Count == 1:
                ReplaceWithGeneratedColumn(modelBuilder, index, filter);
                return;
            default:
                throw new NotSupportedException(
                    $"Index {index.GetDatabaseName()}: filter {filter.Kind} is only supported by MySQL on a unique single-column index.");
        }
    }

    private static void ReplaceWithGeneratedColumn(ModelBuilder modelBuilder, IMutableIndex index, PortableIndexFilter filter)
    {
        var entityType = index.DeclaringEntityType;
        var indexed = index.Properties[0];
        // Los nombres en snake_case los pone una convención al cerrar el modelo, después de este paso: aquí se calcula el
        // mismo nombre con la misma regla (System.Text.Json en snake_case minúscula, como DbIdentifier.ToSnakeCase).
        var indexedColumn = JsonNamingPolicy.SnakeCaseLower.ConvertName(indexed.GetColumnName());
        var name = index.GetDatabaseName();

        // «is_active» + «company_id» da la columna «active_company_id» y la propiedad oculta «ActiveCompanyId».
        var prefix = filter.Column.StartsWith("is_", StringComparison.Ordinal) ? filter.Column[3..] : filter.Column;
        var column = $"{prefix}_{indexedColumn}";
        var propertyName = string.Concat(column.Split('_').Select(part =>
            part.Length == 0 ? part : char.ToUpper(part[0], CultureInfo.InvariantCulture) + part[1..]));

        entityType.RemoveIndex(index);
        var entity = modelBuilder.Entity(entityType.Name);
        entity.Property(Nullable(indexed.ClrType), propertyName)
            .HasColumnName(column)
            .HasComputedColumnSql($"case when `{filter.Column}` then `{indexedColumn}` end", stored: false);
        entity.HasIndex(propertyName).IsUnique().HasDatabaseName(name);
    }

    private static Type Nullable(Type type) =>
        type.IsValueType && System.Nullable.GetUnderlyingType(type) is null ? typeof(Nullable<>).MakeGenericType(type) : type;
}
