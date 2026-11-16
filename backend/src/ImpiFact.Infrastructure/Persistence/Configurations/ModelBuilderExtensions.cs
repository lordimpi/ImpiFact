using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ImpiFact.Infrastructure.Persistence.Configurations;

/// <summary>Ayudas comunes de las configuraciones de entidades (Modelo de datos detallado, secciones 1, 2 y 7).</summary>
internal static class ModelBuilderExtensions
{
    /// <summary>
    /// Valor por defecto en la base (<c>DEFAULT</c>) que EF no usa al insertar: EF siempre escribe el valor de la
    /// propiedad. Sin esto, EF omitiría la columna cuando el valor es el de C# (<c>false</c>, <c>0</c>) y la base
    /// pondría el suyo; por ejemplo, un <c>is_active = false</c> se guardaría como <c>true</c>. La entidad inicia
    /// la propiedad con el mismo valor por defecto.
    /// </summary>
    public static PropertyBuilder<T> HasStoreDefault<T>(this PropertyBuilder<T> property, T value) =>
        property.HasDefaultValue(value).ValueGeneratedNever();

    /// <summary>Importe de documento: <c>numeric(18,2)</c>.</summary>
    public static PropertyBuilder<decimal> IsAmount(this PropertyBuilder<decimal> property) => property.HasPrecision(18, 2);

    /// <summary>Precio unitario o cantidad: <c>numeric(18,6)</c>.</summary>
    public static PropertyBuilder<decimal> IsQuantity(this PropertyBuilder<decimal> property) => property.HasPrecision(18, 6);

    /// <inheritdoc cref="IsQuantity(PropertyBuilder{decimal})"/>
    public static PropertyBuilder<decimal?> IsQuantity(this PropertyBuilder<decimal?> property) => property.HasPrecision(18, 6);

    /// <summary>Porcentaje o tarifa: <c>numeric(9,4)</c>.</summary>
    public static PropertyBuilder<decimal?> IsPercentage(this PropertyBuilder<decimal?> property) => property.HasPrecision(9, 4);

    /// <summary>Texto de largo fijo (<c>char(n)</c>), como el dígito de verificación o un resumen SHA-256 en hexadecimal.</summary>
    public static PropertyBuilder<string> IsFixed(this PropertyBuilder<string> property, int length) =>
        property.HasMaxLength(length).IsFixedLength();

    /// <summary>
    /// Llave foránea (referencia a otra tabla) sin propiedad de navegación y sin borrado en cascada: el principio
    /// 6 del modelo prohíbe <c>ON DELETE CASCADE</c> hacia datos fiscales, y aquí se aplica a todas las referencias.
    /// </summary>
    public static ReferenceCollectionBuilder<TPrincipal, TEntity> References<TEntity, TPrincipal>(
        this EntityTypeBuilder<TEntity> builder, System.Linq.Expressions.Expression<Func<TEntity, object?>> foreignKey)
        where TEntity : class
        where TPrincipal : class =>
        builder.HasOne<TPrincipal>().WithMany().HasForeignKey(foreignKey).OnDelete(DeleteBehavior.Restrict);
}
