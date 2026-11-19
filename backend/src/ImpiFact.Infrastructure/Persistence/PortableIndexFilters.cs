using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ImpiFact.Infrastructure.Persistence;

/// <summary>Qué filas entran en un índice parcial (filtrado): las que tienen la columna en verdadero o con valor.</summary>
public enum PortableIndexFilterKind
{
    /// <summary>Solo las filas con la columna booleana en verdadero, como <c>is_active</c>.</summary>
    WhereTrue,

    /// <summary>Solo las filas con la columna no nula, como <c>cufe</c>.</summary>
    WhereNotNull,
}

/// <summary>El filtro de un índice parcial, sin escribirlo en el SQL de ningún motor.</summary>
/// <param name="Column">Nombre de la columna en la base (snake_case), igual en los tres motores.</param>
public sealed record PortableIndexFilter(PortableIndexFilterKind Kind, string Column);

/// <summary>
/// Índices parciales (filtrados) portables (Modelo de datos detallado, sección 8, trampa 1). PostgreSQL y SQL Server
/// escriben el filtro distinto (<c>is_active</c> frente a <c>[is_active] = 1</c>) y MySQL no tiene índices parciales.
/// Las configuraciones comunes declaran solo la intención con <see cref="HasPortableFilter{TEntity}"/>; el personalizador
/// del modelo de cada proyecto <c>ImpiFact.Infrastructure.&lt;Motor&gt;</c> llama a <see cref="Apply"/> y escribe el filtro
/// (o la alternativa del motor, como una columna generada en MySQL).
/// </summary>
public static class PortableIndexFilters
{
    /// <summary>
    /// Anotación de EF Core (Entity Framework) que guarda la intención mientras se arma el modelo. <see cref="Apply"/> la
    /// quita, así no llega a la instantánea de las migraciones de ningún motor.
    /// </summary>
    public const string AnnotationName = "ImpiFact:PortableIndexFilter";

    /// <summary>Declara que el índice solo incluye las filas que cumplen el filtro.</summary>
    public static IndexBuilder<TEntity> HasPortableFilter<TEntity>(
        this IndexBuilder<TEntity> index, PortableIndexFilterKind kind, string column) =>
        index.HasAnnotation(AnnotationName, new PortableIndexFilter(kind, column));

    /// <summary>
    /// Entrega cada índice con filtro portable a <paramref name="apply"/>, que lo escribe con la sintaxis del motor, y
    /// quita la anotación. Lo llama el personalizador del modelo de cada motor, después de las configuraciones comunes.
    /// </summary>
    public static void Apply(ModelBuilder modelBuilder, Action<IMutableIndex, PortableIndexFilter> apply)
    {
        var indexes = modelBuilder.Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetDeclaredIndexes())
            .Where(index => index.FindAnnotation(AnnotationName) is not null)
            .ToList();
        foreach (var index in indexes)
        {
            var filter = (PortableIndexFilter)index.RemoveAnnotation(AnnotationName)!.Value!;
            apply(index, filter);
        }
    }
}
