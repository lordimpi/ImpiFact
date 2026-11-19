using System.Text.Json.Serialization;

namespace ImpiFact.Application.Features.Documents.Calculate;

/// <summary>
/// Borrador de un documento (factura, nota crédito o nota débito) cuyos totales calcula
/// <c>POST /api/v1/documents/calculate</c>. Trae solo lo que entra en el cálculo: las líneas con su cantidad, su precio,
/// sus descuentos y cargos y sus impuestos, los descuentos y cargos globales y los anticipos. Los nombres JSON van en
/// snake_case, como el resto de <c>/api/v1</c>. Los importes son números JSON en pesos (o en la moneda del documento).
/// </summary>
/// <remarks>
/// <para>
/// Las listas no son anulables en el contrato (una lista que falta vale vacía): así el documento OpenAPI las describe
/// como <c>array</c> y el generador del cliente del panel crea los modelos de sus elementos. El código igual tolera un
/// <c>null</c> explícito en el JSON.
/// </para>
/// <para>
/// No es un comando (<c>ICommand</c>): el cálculo no cambia datos, así que no abre transacción. Sí exige una empresa en
/// la credencial, como todo caso de uso que no se marca con <c>ICompanyOptional</c>.
/// </para>
/// </remarks>
public sealed record CalculateDocumentTotalsRequest
{
    /// <summary><c>lines</c>: las líneas del documento, en el orden en que se emitirán. Al menos una.</summary>
    [JsonPropertyName("lines")]
    public IReadOnlyList<CalculationLineRequest> Lines { get; init; } = [];

    /// <summary>
    /// <c>allowance_charges</c>: descuentos y cargos globales (de la cabecera). No cambian la base de los impuestos:
    /// se restan o se suman al final, en el valor a pagar.
    /// </summary>
    [JsonPropertyName("allowance_charges")]
    public IReadOnlyList<CalculationAllowanceChargeRequest> AllowanceCharges { get; init; } = [];

    /// <summary><c>prepaid_payments</c>: anticipos ya recibidos.</summary>
    [JsonPropertyName("prepaid_payments")]
    public IReadOnlyList<CalculationPrepaidPaymentRequest> PrepaidPayments { get; init; } = [];
}

/// <summary>Una línea del borrador.</summary>
public sealed record CalculationLineRequest
{
    /// <summary><c>quantity</c>: cantidad facturada, mayor que cero y con hasta 6 decimales.</summary>
    [JsonPropertyName("quantity")]
    public decimal? Quantity { get; init; }

    /// <summary>
    /// <c>unit_price</c>: precio de una unidad, sin impuestos. Admite más de 2 decimales: el valor de la línea se
    /// calcula con el precio completo y se redondea después, como lo hacía el PHP.
    /// </summary>
    [JsonPropertyName("unit_price")]
    public decimal? UnitPrice { get; init; }

    /// <summary>
    /// <c>free_of_charge</c>: línea gratuita (una muestra o un obsequio). Su valor es 0 y no admite descuentos, cargos
    /// ni impuestos porcentuales; el precio solo queda como precio de referencia.
    /// </summary>
    [JsonPropertyName("free_of_charge")]
    public bool FreeOfCharge { get; init; }

    /// <summary><c>allowance_charges</c>: descuentos y cargos de la línea; cambian su valor y la base de sus impuestos.</summary>
    [JsonPropertyName("allowance_charges")]
    public IReadOnlyList<CalculationAllowanceChargeRequest> AllowanceCharges { get; init; } = [];

    /// <summary><c>taxes</c>: impuestos de la línea, porcentuales o por unidad.</summary>
    [JsonPropertyName("taxes")]
    public IReadOnlyList<CalculationTaxRequest> Taxes { get; init; } = [];
}

/// <summary>
/// Un descuento o un cargo, de la línea o global. Lleva <c>amount</c> o <c>percent</c>, nunca los dos.
/// </summary>
public sealed record CalculationAllowanceChargeRequest
{
    /// <summary><c>charge_indicator</c>: verdadero si es un cargo; falso si es un descuento.</summary>
    [JsonPropertyName("charge_indicator")]
    public bool? ChargeIndicator { get; init; }

    /// <summary><c>amount</c>: valor del descuento o cargo, con hasta 2 decimales.</summary>
    [JsonPropertyName("amount")]
    public decimal? Amount { get; init; }

    /// <summary>
    /// <c>percent</c>: porcentaje sobre la base, de 0 a 100 y con hasta 2 decimales. La base de un descuento de línea es
    /// precio × cantidad; la de uno global, el valor bruto del documento.
    /// </summary>
    [JsonPropertyName("percent")]
    public decimal? Percent { get; init; }
}

/// <summary>
/// Un impuesto de la línea. Es porcentual (<c>percent</c>, por ejemplo el IVA del 19 %) o de valor fijo por unidad
/// (<c>per_unit_amount</c>, por ejemplo el impuesto a las bolsas plásticas), nunca los dos.
/// </summary>
public sealed record CalculationTaxRequest
{
    /// <summary><c>tax_id</c>: el impuesto en el catálogo de la DIAN (1 IVA, 4 INC, 10 bolsas…).</summary>
    [JsonPropertyName("tax_id")]
    public int? TaxId { get; init; }

    /// <summary><c>percent</c>: tarifa en porcentaje, de 0 a 100 y con hasta 2 decimales.</summary>
    [JsonPropertyName("percent")]
    public decimal? Percent { get; init; }

    /// <summary><c>per_unit_amount</c>: valor del impuesto por unidad, con hasta 2 decimales.</summary>
    [JsonPropertyName("per_unit_amount")]
    public decimal? PerUnitAmount { get; init; }

    /// <summary>
    /// <c>base_unit_measure</c>: unidades gravadas del impuesto por unidad, mayor que cero y con hasta 6 decimales. Sin
    /// él se toma la cantidad de la línea.
    /// </summary>
    [JsonPropertyName("base_unit_measure")]
    public decimal? BaseUnitMeasure { get; init; }

    /// <summary>
    /// <c>unit_measure_id</c>: unidad de las unidades gravadas en el catálogo de la DIAN (70 es «unidad»). No entra en el
    /// cálculo: se devuelve tal cual para armar la emisión.
    /// </summary>
    [JsonPropertyName("unit_measure_id")]
    public int? UnitMeasureId { get; init; }
}

/// <summary>Un anticipo.</summary>
public sealed record CalculationPrepaidPaymentRequest
{
    /// <summary><c>paid_amount</c>: valor recibido por anticipado, con hasta 2 decimales.</summary>
    [JsonPropertyName("paid_amount")]
    public decimal? PaidAmount { get; init; }
}
