using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ImpiFact.Infrastructure.Persistence.DataProtection;

/// <summary>
/// Tabla <c>data_protection_keys</c> del módulo Platform (Modelo de datos detallado, sección 3.7; ADR-25): las
/// llaves de ASP.NET Core Data Protection (el sistema que cifra los campos <c>*_cipher</c>), compartidas por
/// todas las instancias. La columna <c>xml</c> guarda cada llave ya cifrada con la llave maestra.
/// </summary>
internal static class DataProtectionKeyMapping
{
    public static void Configure(EntityTypeBuilder<DataProtectionKey> key)
    {
        key.ToTable("data_protection_keys");
        key.HasKey(k => k.Id);
        key.Property(k => k.FriendlyName).HasMaxLength(200);
        key.Property(k => k.Xml);
    }
}
