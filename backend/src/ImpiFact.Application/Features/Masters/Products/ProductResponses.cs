using System.Text.Json.Serialization;
using ImpiFact.Domain.Masters;

namespace ImpiFact.Application.Features.Masters.Products;

// Respuestas de /api/v1/products (tabla items). Nunca se expone la entidad (Arquitectura detallada, sección 4.2).

/// <summary>Un producto o servicio de la empresa, listo para llenar una línea de la emisión.</summary>
public sealed record ProductResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>Código propio del producto (<c>code</c> de la línea de la emisión); único en la empresa.</summary>
    [JsonPropertyName("code")]
    public string Code { get; init; } = "";

    /// <summary>Nombre que va en la línea de la emisión (<c>description</c> de la línea).</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>Detalle adicional, solo para la empresa.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Unidad de medida (catálogo <c>unit_measures</c>; 70 es «unidad»).</summary>
    [JsonPropertyName("unit_measure_id")]
    public int UnitMeasureId { get; init; }

    /// <summary>Estándar del código (catálogo <c>type_item_identifications</c>; 4 es el estándar propio del contribuyente).</summary>
    [JsonPropertyName("type_item_identification_id")]
    public int TypeItemIdentificationId { get; init; }

    /// <summary>Precio unitario sin impuestos.</summary>
    [JsonPropertyName("unit_price")]
    public decimal UnitPrice { get; init; }

    /// <summary>Impuesto (catálogo <c>taxes</c>; 1 es IVA); nulo si no lleva.</summary>
    [JsonPropertyName("tax_id")]
    public int? TaxId { get; init; }

    /// <summary>Tarifa del impuesto en porcentaje (19 para el IVA del 19 %).</summary>
    [JsonPropertyName("tax_percent")]
    public decimal? TaxPercent { get; init; }

    /// <summary>Valor del impuesto por unidad, en los impuestos que no van en porcentaje.</summary>
    [JsonPropertyName("tax_per_unit_amount")]
    public decimal? TaxPerUnitAmount { get; init; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; init; }

    internal static ProductResponse From(Item item) => new()
    {
        Id = item.Id,
        Code = item.Code,
        Name = item.Name,
        Description = item.Description,
        UnitMeasureId = item.UnitMeasureId,
        TypeItemIdentificationId = item.TypeItemIdentificationId,
        UnitPrice = item.SaleUnitPrice,
        TaxId = item.TaxId,
        TaxPercent = item.TaxPercent,
        TaxPerUnitAmount = item.TaxPerUnitAmount,
        IsActive = item.IsActive,
    };
}

/// <summary>Una página de productos en el orden pedido.</summary>
public sealed record ProductListResponse
{
    [JsonPropertyName("items")]
    public IReadOnlyList<ProductResponse> Items { get; init; } = [];

    /// <summary>Texto opaco para pedir la página siguiente (<c>?cursor=</c>, con el mismo <c>sort</c>); nulo en la última.</summary>
    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; init; }
}
