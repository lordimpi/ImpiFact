using ImpiFact.Domain.Catalog;
using ImpiFact.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ImpiFact.Infrastructure.Persistence.Configurations.Catalog;

/// <summary>Cómo se indexa la columna <c>code</c> de un catálogo.</summary>
internal enum CodeIndex
{
    /// <summary><c>ux_&lt;catalogo&gt;_code</c>: el código no se repite.</summary>
    Unique,

    /// <summary><c>ix_&lt;catalogo&gt;_code</c>: el PHP repite códigos o los distingue solo por mayúsculas.</summary>
    NonUnique,

    /// <summary>Sin índice propio sobre <c>code</c> (municipios: su único es por departamento y código).</summary>
    None,
}

/// <summary>
/// Forma base de un catálogo DIAN (Modelo de datos detallado, sección 3.1): <c>id int</c> explícito (sin
/// generación), <c>code varchar(20)</c>, <c>name varchar(255)</c>, <c>is_active</c> con valor por defecto
/// <c>true</c>, vigencia opcional e índice sobre <c>code</c>. Siembra las filas del JSON de su tabla.
/// </summary>
internal abstract class CatalogConfiguration<T>(string table, CodeIndex codeIndex = CodeIndex.Unique, int codeLength = 20)
    : IEntityTypeConfiguration<T>
    where T : CatalogEntry
{
    public void Configure(EntityTypeBuilder<T> builder)
    {
        builder.ToTable(table);
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.Code).HasMaxLength(codeLength);
        builder.Property(entry => entry.Name).HasMaxLength(255);
        builder.Property(entry => entry.IsActive).HasStoreDefault(true);

        switch (codeIndex)
        {
            case CodeIndex.Unique:
                builder.HasIndex(entry => entry.Code).IsUnique();
                break;
            case CodeIndex.NonUnique:
                builder.HasIndex(entry => entry.Code);
                break;
        }

        ConfigureExtras(builder);
        builder.HasData(CatalogSeed.Load<T>(table));
    }

    /// <summary>Columnas, índices y referencias propias del catálogo.</summary>
    protected virtual void ConfigureExtras(EntityTypeBuilder<T> builder)
    {
    }
}
