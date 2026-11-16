using ImpiFact.Dian.Cufe;

namespace ImpiFact.Dian.Ubl;

/// <summary>Tipo de nota sobre una factura electrónica de venta.</summary>
public enum NoteKind
{
    /// <summary>Nota crédito (tipo 91 de la DIAN, la autoridad tributaria): disminuye el valor facturado.</summary>
    Credit,

    /// <summary>Nota débito (tipo 92 de la DIAN): aumenta el valor facturado.</summary>
    Debit,
}

/// <summary>
/// Entrada de <see cref="NoteUblBuilder"/>: una nota crédito o débito de factura electrónica de venta. Usa
/// las mismas partes, impuestos, totales y líneas que <see cref="SalesInvoice"/>, con cada valor de los
/// catálogos (listas de códigos de la DIAN) ya convertido al código y al nombre que se escriben en el XML
/// (decisión de arquitectura ADR-26). Los valores son <see cref="decimal"/> sin redondear.
/// </summary>
/// <remarks>
/// A diferencia de la factura, la nota no lleva la resolución de numeración en el XML (no hay
/// sts:InvoiceControl) y su CUDE (código único de documento electrónico) se calcula con el PIN del
/// software, no con la clave técnica de la resolución. Por eso la nota solo pide el prefijo y el número.
/// </remarks>
public sealed record SalesNote
{
    public required NoteKind Kind { get; init; }

    public required DianEnvironment Environment { get; init; }

    /// <summary>
    /// Inicio de la URL de consulta del documento en el portal de la DIAN; el CUDE se agrega al final y el
    /// resultado va en sts:QRCode. Sale de la configuración por ambiente, no del código.
    /// </summary>
    public required string QrCodeBaseUrl { get; init; }

    /// <summary>
    /// Código del tipo de operación que se escribe en cbc:CustomizationID: 20 nota crédito que referencia
    /// una factura, 22 nota crédito sin referencia, 30 nota débito que referencia una factura, 32 nota
    /// débito sin referencia.
    /// </summary>
    public required string OperationTypeCode { get; init; }

    /// <summary>Prefijo de la numeración de notas (por ejemplo NC o ND); puede estar vacío.</summary>
    public required string Prefix { get; init; }

    /// <summary>Número consecutivo; el ID del documento es <see cref="Prefix"/> seguido de él.</summary>
    public required long Number { get; init; }

    public required DateOnly IssueDate { get; init; }

    /// <summary>Hora local de Colombia; el XML siempre lleva el desfase -05:00.</summary>
    public required TimeOnly IssueTime { get; init; }

    /// <summary>Texto libre que se escribe en cbc:Note cuando no es null.</summary>
    public string? Note { get; init; }

    public required SoftwareCredentials Software { get; init; }

    /// <summary>Moneda de la empresa emisora (COP).</summary>
    public required string LocalCurrencyCode { get; init; }

    /// <summary>Se asigna cuando la nota se emite en otra moneda.</summary>
    public ForeignCurrency? ForeignCurrency { get; init; }

    public required InvoiceParty Supplier { get; init; }

    public required InvoiceParty Customer { get; init; }

    /// <summary>Solo se usa en cac:PaymentMeans; las notas no llevan cac:PaymentTerms.</summary>
    public required PaymentTerms Payment { get; init; }

    /// <summary>Periodo facturado (cac:InvoicePeriod); lo exige la DIAN en las notas sin referencia.</summary>
    public InvoicePeriod? InvoicePeriod { get; init; }

    /// <summary>Motivo de la nota (cac:DiscrepancyResponse); cuando es null se omite el bloque.</summary>
    public DiscrepancyResponse? Discrepancy { get; init; }

    /// <summary>Factura a la que se refiere la nota; null en las notas sin referencia.</summary>
    public BillingReference? BillingReference { get; init; }

    /// <summary>Descuentos y cargos a nivel de documento.</summary>
    public IReadOnlyList<AllowanceCharge> AllowanceCharges { get; init; } = [];

    /// <summary>Subtotales de impuestos; se agrupan por tipo de impuesto (esquema tributario) en cac:TaxTotal.</summary>
    public IReadOnlyList<TaxSubtotal> TaxSubtotals { get; init; } = [];

    /// <summary>Subtotales de retenciones; se agrupan en cac:WithholdingTaxTotal.</summary>
    public IReadOnlyList<TaxSubtotal> WithholdingSubtotals { get; init; } = [];

    /// <summary>
    /// Totales: cac:LegalMonetaryTotal en la nota crédito y cac:RequestedMonetaryTotal en la débito. El
    /// CUDE toma de aquí el valor antes de impuestos y el total a pagar.
    /// </summary>
    public required MonetaryTotals Totals { get; init; }

    public required IReadOnlyList<InvoiceLine> Lines { get; init; }

    /// <summary>Número del documento tal como se escribe en cbc:ID.</summary>
    public string DocumentNumber => Prefix + Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Periodo al que corresponde la nota.</summary>
public sealed record InvoicePeriod(DateOnly StartDate, DateOnly EndDate);

/// <summary>
/// Motivo de la nota: el código del catálogo de discrepancias de la DIAN (uno para notas crédito y otro
/// para notas débito) y su nombre en ese catálogo, que es lo que se escribe en cbc:Description.
/// </summary>
public sealed record DiscrepancyResponse(string Code, string Description);

/// <summary>Factura a la que se refiere la nota (cac:BillingReference).</summary>
/// <param name="Number">Número de la factura, con su prefijo.</param>
/// <param name="Uuid">CUFE de la factura.</param>
/// <param name="IssueDate">Fecha de emisión de la factura.</param>
public sealed record BillingReference(string Number, string Uuid, DateOnly IssueDate)
{
    /// <summary>Algoritmo del código referenciado; CUFE-SHA384 para una factura electrónica de venta.</summary>
    public string SchemeName { get; init; } = "CUFE-SHA384";
}
