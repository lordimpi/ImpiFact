using System.Text.Json.Serialization;

namespace ImpiFact.Application.Features.Documents.Calculate;

/// <summary>
/// Respuesta de <c>POST /api/v1/documents/calculate</c>: los valores que el panel muestra y luego envía tal cual al
/// emitir. Cada importe ya viene redondeado a 2 decimales, como queda escrito en el XML del documento, así que el
/// cliente no hace ninguna cuenta. Las listas conservan el orden de la solicitud.
/// </summary>
/// <remarks>
/// Cómo se pasa a la emisión (contrato de ApiDian): <c>lines[i]</c> llena <c>line_extension_amount</c>,
/// <c>allowance_charges</c> (con <c>base_amount</c>) y <c>tax_totals</c> (los <c>tax_subtotals</c> de la línea) de la
/// línea <c>i</c>; el <c>tax_totals</c> de la cabecera es la lista de todos los <c>tax_subtotals</c> de
/// <see cref="TaxTotals"/>, en orden; <see cref="LegalMonetaryTotals"/> va como <c>legal_monetary_totals</c> en la
/// factura y la nota crédito, y como <c>requested_monetary_totals</c> en la nota débito.
/// </remarks>
public sealed record DocumentTotalsCalculation
{
    /// <summary><c>lines</c>: el resultado de cada línea, en el orden de la solicitud.</summary>
    [JsonPropertyName("lines")]
    public required IReadOnlyList<CalculatedLine> Lines { get; init; }

    /// <summary>
    /// <c>tax_totals</c>: impuestos del documento agrupados como en el XML de la DIAN: un grupo por <c>tax_id</c>
    /// (cac:TaxTotal), con un subtotal por tarifa o por valor por unidad (cac:TaxSubtotal). Los grupos y los subtotales
    /// salen en el orden en que aparecen por primera vez en las líneas.
    /// </summary>
    [JsonPropertyName("tax_totals")]
    public required IReadOnlyList<CalculatedTaxTotal> TaxTotals { get; init; }

    /// <summary><c>allowance_charges</c>: los descuentos y cargos globales con su valor y su base.</summary>
    [JsonPropertyName("allowance_charges")]
    public required IReadOnlyList<CalculatedAllowanceCharge> AllowanceCharges { get; init; }

    /// <summary><c>legal_monetary_totals</c>: los totales del documento.</summary>
    [JsonPropertyName("legal_monetary_totals")]
    public required CalculatedMonetaryTotals LegalMonetaryTotals { get; init; }
}

/// <summary>Resultado de una línea.</summary>
public sealed record CalculatedLine
{
    /// <summary>
    /// <c>line_extension_amount</c>: valor de la línea sin impuestos: precio × cantidad − descuentos + cargos de la
    /// línea (0 en una línea gratuita).
    /// </summary>
    [JsonPropertyName("line_extension_amount")]
    public required decimal LineExtensionAmount { get; init; }

    /// <summary><c>allowance_charges</c>: los descuentos y cargos de la línea con su valor y su base.</summary>
    [JsonPropertyName("allowance_charges")]
    public required IReadOnlyList<CalculatedAllowanceCharge> AllowanceCharges { get; init; }

    /// <summary><c>tax_subtotals</c>: un subtotal por cada impuesto de la línea, en el orden de la solicitud.</summary>
    [JsonPropertyName("tax_subtotals")]
    public required IReadOnlyList<CalculatedTaxSubtotal> TaxSubtotals { get; init; }
}

/// <summary>Un descuento o cargo ya calculado.</summary>
public sealed record CalculatedAllowanceCharge
{
    /// <summary><c>charge_indicator</c>: verdadero si es un cargo; falso si es un descuento.</summary>
    [JsonPropertyName("charge_indicator")]
    public required bool ChargeIndicator { get; init; }

    /// <summary><c>amount</c>: valor del descuento o cargo.</summary>
    [JsonPropertyName("amount")]
    public required decimal Amount { get; init; }

    /// <summary>
    /// <c>base_amount</c>: base sobre la que se aplica: precio × cantidad en la línea; el valor bruto del documento en
    /// los globales.
    /// </summary>
    [JsonPropertyName("base_amount")]
    public required decimal BaseAmount { get; init; }
}

/// <summary>Un grupo de impuestos del documento (un cac:TaxTotal del XML): todos los subtotales de un mismo impuesto.</summary>
public sealed record CalculatedTaxTotal
{
    /// <summary><c>tax_id</c>: el impuesto.</summary>
    [JsonPropertyName("tax_id")]
    public required int TaxId { get; init; }

    /// <summary><c>tax_amount</c>: la suma de los subtotales del grupo.</summary>
    [JsonPropertyName("tax_amount")]
    public required decimal TaxAmount { get; init; }

    /// <summary><c>tax_subtotals</c>: un subtotal por tarifa (o por valor por unidad), sumando las líneas que la usan.</summary>
    [JsonPropertyName("tax_subtotals")]
    public required IReadOnlyList<CalculatedTaxSubtotal> TaxSubtotals { get; init; }
}

/// <summary>
/// Un subtotal de impuesto (un cac:TaxSubtotal del XML), con los mismos nombres que un elemento de <c>tax_totals</c>
/// en la emisión. Lleva <c>percent</c> si es porcentual, o <c>per_unit_amount</c>, <c>base_unit_measure</c> y
/// <c>unit_measure_id</c> si es por unidad; los otros campos van en <see langword="null"/>.
/// </summary>
public sealed record CalculatedTaxSubtotal
{
    /// <summary><c>tax_id</c>: el impuesto.</summary>
    [JsonPropertyName("tax_id")]
    public required int TaxId { get; init; }

    /// <summary><c>taxable_amount</c>: base gravable (el valor de la línea, o la suma de las líneas en la cabecera).</summary>
    [JsonPropertyName("taxable_amount")]
    public required decimal TaxableAmount { get; init; }

    /// <summary><c>tax_amount</c>: valor del impuesto.</summary>
    [JsonPropertyName("tax_amount")]
    public required decimal TaxAmount { get; init; }

    /// <summary><c>percent</c>: tarifa en porcentaje.</summary>
    [JsonPropertyName("percent")]
    public decimal? Percent { get; init; }

    /// <summary><c>per_unit_amount</c>: valor por unidad.</summary>
    [JsonPropertyName("per_unit_amount")]
    public decimal? PerUnitAmount { get; init; }

    /// <summary><c>base_unit_measure</c>: unidades gravadas (en la cabecera, la suma de las líneas).</summary>
    [JsonPropertyName("base_unit_measure")]
    public decimal? BaseUnitMeasure { get; init; }

    /// <summary><c>unit_measure_id</c>: unidad de las unidades gravadas, tal como vino en la solicitud.</summary>
    [JsonPropertyName("unit_measure_id")]
    public int? UnitMeasureId { get; init; }
}

/// <summary>Totales del documento, con los nombres de <c>legal_monetary_totals</c> de la emisión.</summary>
public sealed record CalculatedMonetaryTotals
{
    /// <summary><c>line_extension_amount</c>: valor bruto, la suma del valor de las líneas.</summary>
    [JsonPropertyName("line_extension_amount")]
    public required decimal LineExtensionAmount { get; init; }

    /// <summary><c>tax_exclusive_amount</c>: base gravable, la suma del valor de las líneas que llevan algún impuesto.</summary>
    [JsonPropertyName("tax_exclusive_amount")]
    public required decimal TaxExclusiveAmount { get; init; }

    /// <summary><c>tax_inclusive_amount</c>: valor bruto más los impuestos de <c>tax_totals</c>.</summary>
    [JsonPropertyName("tax_inclusive_amount")]
    public required decimal TaxInclusiveAmount { get; init; }

    /// <summary><c>allowance_total_amount</c>: suma de los descuentos globales.</summary>
    [JsonPropertyName("allowance_total_amount")]
    public required decimal AllowanceTotalAmount { get; init; }

    /// <summary><c>charge_total_amount</c>: suma de los cargos globales.</summary>
    [JsonPropertyName("charge_total_amount")]
    public required decimal ChargeTotalAmount { get; init; }

    /// <summary><c>pre_paid_amount</c>: suma de los anticipos. Es informativo: no se resta del valor a pagar.</summary>
    [JsonPropertyName("pre_paid_amount")]
    public required decimal PrepaidAmount { get; init; }

    /// <summary>
    /// <c>payable_amount</c>: valor a pagar = total con impuestos − descuentos globales + cargos globales (regla AU14
    /// del anexo técnico de la DIAN; los anticipos no entran).
    /// </summary>
    [JsonPropertyName("payable_amount")]
    public required decimal PayableAmount { get; init; }
}
