namespace ImpiFact.Application.Features.Documents.Totals;

/// <summary>Tipo de documento electrónico cuyos totales se validan.</summary>
public enum TotalsDocumentKind
{
    /// <summary>Factura de venta nacional; reglas FA* del anexo técnico y totales en cac:LegalMonetaryTotal.</summary>
    Invoice,

    /// <summary>Nota crédito; reglas CA* y totales en cac:LegalMonetaryTotal.</summary>
    CreditNote,

    /// <summary>Nota débito; reglas DA* y totales en cac:RequestedMonetaryTotal.</summary>
    DebitNote,
}

/// <summary>
/// Entrada de <see cref="DianTotalsValidator"/>: los totales, impuestos, descuentos, cargos, anticipos y
/// líneas de un documento tal como los envió el cliente, sin redondear. Es un modelo neutro: los casos de
/// uso lo arman desde la solicitud legada respetando el orden de cada lista, porque el validador nombra los
/// campos con errores por su posición en esa solicitud (por ejemplo <c>invoice_lines[2].tax_totals[0]</c>).
/// </summary>
/// <remarks>
/// Cada importe se redondea a 2 decimales antes de compararlo, igual que al escribirlo en el XML (UBL,
/// el formato XML de la DIAN), porque la DIAN (Dirección de Impuestos y Aduanas Nacionales) valida el XML,
/// no la solicitud.
/// </remarks>
public sealed record TotalsDocument
{
    public required TotalsDocumentKind Kind { get; init; }

    /// <summary>
    /// <c>legal_monetary_totals</c> de la factura y la nota crédito, o <c>requested_monetary_totals</c> de la
    /// nota débito.
    /// </summary>
    public required MonetaryTotalsInput MonetaryTotals { get; init; }

    /// <summary>
    /// <c>tax_totals</c> de la cabecera, en el orden de la solicitud. En el XML se agrupan por
    /// <see cref="TaxSubtotalInput.TaxId"/> en un cac:TaxTotal cuyo valor es la suma de sus subtotales.
    /// </summary>
    public IReadOnlyList<TaxSubtotalInput> TaxTotals { get; init; } = [];

    /// <summary><c>allowance_charges</c> de la cabecera (descuentos y cargos globales).</summary>
    public IReadOnlyList<AllowanceChargeInput> AllowanceCharges { get; init; } = [];

    /// <summary>Anticipos (<c>prepaid_payments</c>); cada uno es un cac:PrepaidPayment del XML.</summary>
    public IReadOnlyList<PrepaidPaymentInput> PrepaidPayments { get; init; } = [];

    /// <summary>
    /// <c>invoice_lines</c>, <c>credit_note_lines</c> o <c>debit_note_lines</c>, según
    /// <see cref="Kind"/>.
    /// </summary>
    public IReadOnlyList<TotalsLineInput> Lines { get; init; } = [];
}

/// <summary>
/// Totales del documento. Un total opcional es <see langword="null"/> cuando el XML no lleva el elemento.
/// </summary>
public sealed record MonetaryTotalsInput
{
    /// <summary><c>line_extension_amount</c>: valor bruto antes de impuestos.</summary>
    public required decimal LineExtensionAmount { get; init; }

    /// <summary><c>tax_exclusive_amount</c>: base gravable total.</summary>
    public required decimal TaxExclusiveAmount { get; init; }

    /// <summary><c>tax_inclusive_amount</c>: valor bruto más impuestos.</summary>
    public required decimal TaxInclusiveAmount { get; init; }

    /// <summary><c>allowance_total_amount</c>: total de descuentos de la cabecera.</summary>
    public decimal? AllowanceTotalAmount { get; init; }

    /// <summary><c>charge_total_amount</c>: total de cargos de la cabecera.</summary>
    public decimal? ChargeTotalAmount { get; init; }

    /// <summary><c>pre_paid_amount</c>: total de anticipos.</summary>
    public decimal? PrepaidAmount { get; init; }

    /// <summary><c>payable_amount</c>: valor a pagar.</summary>
    public required decimal PayableAmount { get; init; }
}

/// <summary>
/// Un subtotal de impuesto (un elemento de <c>tax_totals</c>), que en el XML es un cac:TaxSubtotal.
/// </summary>
public sealed record TaxSubtotalInput
{
    /// <summary>
    /// <c>tax_id</c>: el impuesto. Los subtotales con el mismo valor forman un solo cac:TaxTotal, como en el
    /// PHP (php-rules, sección 3.2).
    /// </summary>
    public required int TaxId { get; init; }

    /// <summary><c>taxable_amount</c>: base gravable.</summary>
    public required decimal TaxableAmount { get; init; }

    /// <summary><c>tax_amount</c>: valor del impuesto.</summary>
    public required decimal TaxAmount { get; init; }

    /// <summary><c>percent</c>: tarifa en porcentaje (19.00 para el IVA del 19 %); nulo en los impuestos por unidad.</summary>
    public decimal? Percent { get; init; }

    /// <summary><c>per_unit_amount</c>: valor del impuesto por unidad, en los impuestos de valor fijo.</summary>
    public decimal? PerUnitAmount { get; init; }

    /// <summary><c>base_unit_measure</c>: unidades gravadas, en los impuestos de valor fijo.</summary>
    public decimal? BaseUnitMeasure { get; init; }
}

/// <summary>Un descuento o cargo (<c>allowance_charges[i]</c>).</summary>
/// <param name="IsCharge"><c>charge_indicator</c>: verdadero si es un cargo, falso si es un descuento.</param>
/// <param name="Amount"><c>amount</c>: valor del descuento o cargo.</param>
public sealed record AllowanceChargeInput(bool IsCharge, decimal Amount);

/// <summary>Un anticipo.</summary>
/// <param name="PaidAmount"><c>paidamount</c>: valor pagado por anticipado.</param>
public sealed record PrepaidPaymentInput(decimal PaidAmount, string? Field = null);

/// <summary>Una línea del documento.</summary>
public sealed record TotalsLineInput
{
    /// <summary><c>invoiced_quantity</c>: cantidad.</summary>
    public required decimal Quantity { get; init; }

    /// <summary><c>line_extension_amount</c>: valor total de la línea sin impuestos.</summary>
    public required decimal LineExtensionAmount { get; init; }

    /// <summary><c>price_amount</c>: precio unitario.</summary>
    public required decimal PriceAmount { get; init; }

    /// <summary><c>base_quantity</c>: cantidad a la que se refiere el precio.</summary>
    public required decimal BaseQuantity { get; init; }

    /// <summary><c>allowance_charges</c> de la línea.</summary>
    public IReadOnlyList<AllowanceChargeInput> AllowanceCharges { get; init; } = [];

    /// <summary><c>tax_totals</c> de la línea, en el orden de la solicitud.</summary>
    public IReadOnlyList<TaxSubtotalInput> TaxTotals { get; init; } = [];
}
