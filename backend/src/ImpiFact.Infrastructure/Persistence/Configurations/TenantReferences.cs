using ImpiFact.Domain.Common;
using ImpiFact.Domain.Companies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ImpiFact.Infrastructure.Persistence.Configurations;

/// <summary>Referencias entre tablas de empresa (Modelo de datos detallado, principio 1 y sección 7).</summary>
internal static class TenantReferences
{
    /// <summary>
    /// Llave foránea compuesta <c>(company_id, X_id)</c> hacia la llave alterna <c>(company_id, id)</c> de una tabla
    /// de la misma empresa (Modelo de datos detallado, sección 7): una fila no puede apuntar a otra empresa.
    /// La tabla padre declara esa llave alterna una sola vez con <see cref="HasCompanyScopedKey{TEntity}"/>.
    /// </summary>
    /// <param name="foreignKeyProperty">Propiedad con el id de la fila padre, por ejemplo <c>DocumentId</c>.</param>
    public static ReferenceCollectionBuilder<TPrincipal, TEntity> ReferencesInCompany<TEntity, TPrincipal>(
        this EntityTypeBuilder<TEntity> builder, string foreignKeyProperty)
        where TEntity : class, ITenantOwned
        where TPrincipal : class, ITenantOwned =>
        builder.HasOne<TPrincipal>().WithMany()
            .HasForeignKey(nameof(ITenantOwned.CompanyId), foreignKeyProperty)
            .HasPrincipalKey(nameof(ITenantOwned.CompanyId), "Id")
            .OnDelete(DeleteBehavior.Restrict);

    /// <summary>
    /// Llave alterna <c>UQ (company_id, id)</c> (se nombra <c>ux_&lt;tabla&gt;_company_id_id</c>), destino de las llaves
    /// foráneas compuestas. Se declara solo como llave alterna, nunca además como índice único, para no repetir la
    /// misma restricción con el mismo nombre.
    /// </summary>
    public static void HasCompanyScopedKey<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : TenantEntity =>
        builder.HasAlternateKey(entity => new { entity.CompanyId, entity.Id });

    /// <summary>Columna <c>company_id</c> con su referencia a <c>companies</c>.</summary>
    public static void BelongsToCompany<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class, ITenantOwned =>
        builder.References<TEntity, Company>(entity => entity.CompanyId);
}
