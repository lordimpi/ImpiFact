using ImpiFact.Domain.Common;

namespace ImpiFact.Domain.Masters;

// Terceros y productos (Modelo de datos detallado, sección 3.4). Ambos admiten borrado lógico.

/// <summary>
/// Cliente de la empresa (<c>customers</c>). Único por empresa, tipo y número de identificación; sin contraseña
/// (el portal del cliente usa <c>users</c>). Al crear la empresa se siembra el consumidor final.
/// </summary>
public sealed class Customer : TenantEntity, ISoftDeletable
{
    public required string IdentificationNumber { get; set; }

    public string? Dv { get; set; }

    public int TypeDocumentIdentificationId { get; set; }

    public required string Name { get; set; }

    public string? Phone { get; set; }

    public string? Address { get; set; }

    public string? Email { get; set; }

    public int? MunicipalityId { get; set; }

    public int? TypeOrganizationId { get; set; }

    public int? TypeRegimeId { get; set; }

    public int? TypeLiabilityId { get; set; }

    public string? MerchantRegistration { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>
/// Producto o servicio (<c>items</c>), solo el subconjunto Community: sin inventario, lotes ni comisiones.
/// El código es único por empresa (en el PHP el nombre era único global, un error con varias empresas).
/// </summary>
public sealed class Item : TenantEntity, ISoftDeletable
{
    public required string Code { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public int UnitMeasureId { get; set; }

    public int TypeItemIdentificationId { get; set; }

    public decimal SaleUnitPrice { get; set; }

    /// <summary>Impuesto del producto (<c>taxes</c>); nulo si no lleva impuesto.</summary>
    public int? TaxId { get; set; }

    /// <summary>Tarifa del impuesto en porcentaje (19 para el IVA del 19 %); nula en los impuestos por unidad.</summary>
    public decimal? TaxPercent { get; set; }

    /// <summary>Valor del impuesto por unidad, para los impuestos que no van en porcentaje (INC de bolsas, IC).</summary>
    public decimal? TaxPerUnitAmount { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset? DeletedAt { get; set; }
}
