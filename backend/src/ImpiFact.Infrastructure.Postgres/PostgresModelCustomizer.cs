using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ImpiFact.Infrastructure.Postgres;

/// <summary>
/// Ajustes del modelo propios de PostgreSQL, después de las configuraciones comunes. Las columnas JSON usan
/// <c>jsonb</c>, el tipo JSON binario de PostgreSQL que valida el contenido (Modelo de datos detallado, sección 8).
/// </summary>
internal sealed class PostgresModelCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        var jsonProperties = modelBuilder.Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetDeclaredProperties())
            .Where(property => JsonColumn.IsJsonColumn(property) && property.GetColumnType() is null);
        foreach (var property in jsonProperties)
        {
            property.SetColumnType("jsonb");
        }

        // Índices parciales: PostgreSQL los escribe tal cual, «where is_active» o «where cufe IS NOT NULL».
        PortableIndexFilters.Apply(modelBuilder, (index, filter) => index.SetFilter(filter.Kind switch
        {
            PortableIndexFilterKind.WhereTrue => filter.Column,
            PortableIndexFilterKind.WhereNotNull => $"{filter.Column} IS NOT NULL",
            _ => throw new NotSupportedException($"Index filter {filter.Kind} is not supported."),
        }));
    }
}
