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
    }
}
