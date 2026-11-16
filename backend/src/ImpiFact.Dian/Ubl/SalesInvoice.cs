using ImpiFact.Dian.Cufe;

namespace ImpiFact.Dian.Ubl;

/// <summary>
/// Entrada de <see cref="InvoiceUblBuilder"/>: una factura de venta nacional (tipo 01 de la DIAN) con cada
/// valor de los catálogos (listas de códigos de la DIAN) ya convertido al código y al nombre que se
/// escriben en el XML (decisión de arquitectura ADR-26). Los valores son <see cref="decimal"/> sin
/// redondear; <see cref="InvoiceUblBuilder"/> los redondea con DianRounding al escribirlos.
/// </summary>
public sealed record SalesInvoice
{
    public required DianEnvironment Environment { get; init; }

    /// <summary>
    /// Inicio de la URL de consulta de la factura en el portal de la DIAN; el CUFE se agrega al final y el
    /// resultado va en sts:QRCode. Depende del ambiente (habilitación o producción) y sale de la
    /// configuración (DianOptions), no del código.
    /// </summary>
    public required string QrCodeBaseUrl { get; init; }

    /// <summary>Código del tipo de operación que se escribe en cbc:CustomizationID (10 = estándar).</summary>
    public required string OperationTypeCode { get; init; }

    /// <summary>
    /// Código del tipo de documento que se escribe en cbc:InvoiceTypeCode (01 = factura de venta nacional).
    /// </summary>
    public required string InvoiceTypeCode { get; init; }

    public required NumberingResolution Resolution { get; init; }

    /// <summary>Número consecutivo; el ID del documento es el prefijo de la resolución seguido de él.</summary>
    public required long Number { get; init; }

    public required DateOnly IssueDate { get; init; }

    /// <summary>Hora local de Colombia; el XML siempre lleva el desfase -05:00.</summary>
    public required TimeOnly IssueTime { get; init; }

    public required SoftwareCredentials Software { get; init; }

    /// <summary>Moneda de la empresa emisora (COP). La usan las líneas, los impuestos y los totales.</summary>
    public required string LocalCurrencyCode { get; init; }

    /// <summary>Se asigna cuando la factura se emite en otra moneda.</summary>
    public ForeignCurrency? ForeignCurrency { get; init; }

    /// <summary>
    /// Nota libre de la factura (<c>cbc:Note</c>). Nula si el cliente no la envía; el texto llega ya sin saltos de
    /// línea ni «|», como lo deja el PHP.
    /// </summary>
    public string? Note { get; init; }

    public required InvoiceParty Supplier { get; init; }

    public required InvoiceParty Customer { get; init; }

    public required PaymentTerms Payment { get; init; }

    /// <summary>Descuentos y cargos a nivel de documento.</summary>
    public IReadOnlyList<AllowanceCharge> AllowanceCharges { get; init; } = [];

    /// <summary>
    /// Subtotales de impuestos; <see cref="InvoiceUblBuilder"/> los agrupa por tipo de impuesto (esquema
    /// tributario) en cac:TaxTotal.
    /// </summary>
    public IReadOnlyList<TaxSubtotal> TaxSubtotals { get; init; } = [];

    /// <summary>
    /// Subtotales de retenciones; se agrupan por tipo de impuesto (esquema tributario) en cac:WithholdingTaxTotal.
    /// </summary>
    public IReadOnlyList<TaxSubtotal> WithholdingSubtotals { get; init; } = [];

    public required MonetaryTotals Totals { get; init; }

    public required IReadOnlyList<InvoiceLine> Lines { get; init; }

    /// <summary>Número del documento tal como se escribe en cbc:ID.</summary>
    public string DocumentNumber => Resolution.Prefix + Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Resolución de numeración de la DIAN que autoriza el número de la factura.</summary>
public sealed record NumberingResolution(
    string AuthorizationNumber,
    DateOnly StartDate,
    DateOnly EndDate,
    string Prefix,
    long From,
    long To,
    string TechnicalKey);

/// <summary>Software registrado ante la DIAN; entra en sts:SoftwareID y sts:SoftwareSecurityCode.</summary>
public sealed record SoftwareCredentials(string SoftwareId, string Pin)
{
    /// <summary>No incluye el PIN, para que un registro accidental del objeto no lo filtre.</summary>
    public override string ToString() => $"SoftwareCredentials {{ SoftwareId = {SoftwareId} }}";
}

/// <summary>
/// Moneda extranjera del documento. cac:PaymentExchangeRate solo se escribe cuando se conocen tanto la
/// tasa como su fecha.
/// </summary>
public sealed record ForeignCurrency(string CurrencyCode, decimal? CalculationRate, DateOnly? CalculationRateDate);

/// <summary>Proveedor o cliente.</summary>
public sealed record InvoiceParty
{
    /// <summary>
    /// Código del tipo de organización que se escribe en cbc:AdditionalAccountID (1 persona jurídica,
    /// 2 persona natural).
    /// </summary>
    public required string OrganizationTypeCode { get; init; }

    public required string IdentificationNumber { get; init; }

    /// <summary>
    /// Dígito de verificación del NIT (número de identificación tributaria); cuando no existe se escribe
    /// el atributo schemeID vacío.
    /// </summary>
    public string? CheckDigit { get; init; }

    /// <summary>Código del tipo de identificación (31 NIT, 13 cédula de ciudadanía, ...).</summary>
    public required string IdentificationTypeCode { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// Cuando es null se omiten la ubicación física (cac:PhysicalLocation) y la dirección de registro
    /// (cac:RegistrationAddress).
    /// </summary>
    public PartyAddress? Address { get; init; }

    /// <summary>Código del régimen que se escribe como listName de cbc:TaxLevelCode (48, 49).</summary>
    public required string RegimeCode { get; init; }

    /// <summary>Código de responsabilidad fiscal que se escribe en cbc:TaxLevelCode (O-23, R-99-PN, ...).</summary>
    public required string LiabilityCode { get; init; }

    public required string TaxSchemeCode { get; init; }

    public required string TaxSchemeName { get; init; }

    /// <summary>Número de matrícula mercantil; puede estar vacío.</summary>
    public string MerchantRegistration { get; init; } = "";

    public string? Telephone { get; init; }

    public string? Email { get; init; }

    /// <summary>El consumidor final de la DIAN (222222222222) no lleva datos de contacto.</summary>
    public bool IsFinalConsumer => IdentificationNumber == "222222222222";
}

/// <summary>
/// Dirección de una parte. <see cref="CityName"/> se conserva como está guardado en el catálogo:
/// <see cref="InvoiceUblBuilder"/> le quita los espacios sobrantes en todas partes excepto en la ubicación
/// física del proveedor, donde ApiDian (la implementación de referencia en PHP) lo escribe tal cual.
/// </summary>
public sealed record PartyAddress(
    string MunicipalityCode,
    string CityName,
    string DepartmentName,
    string DepartmentCode,
    string AddressLine,
    string CountryCode,
    string CountryName,
    string LanguageCode);

/// <summary>Forma de pago (contado/crédito) y medio de pago.</summary>
public sealed record PaymentTerms(string FormCode, string MethodCode, DateOnly? DueDate, int? DurationDays);

/// <summary>Un descuento (allowance) o un cargo, a nivel de documento o de línea.</summary>
public sealed record AllowanceCharge
{
    public required bool IsCharge { get; init; }

    /// <summary>Código de motivo de descuento de la DIAN; solo se escribe para descuentos.</summary>
    public string? ReasonCode { get; init; }

    public required string Reason { get; init; }

    /// <summary>
    /// Porcentaje; cuando es null, <see cref="InvoiceUblBuilder"/> usa Amount / BaseAmount * 100 (0 cuando
    /// no hay base).
    /// </summary>
    public decimal? MultiplierFactorNumeric { get; init; }

    public required decimal Amount { get; init; }

    /// <summary>Solo se escribe cuando está presente.</summary>
    public decimal? BaseAmount { get; init; }
}

/// <summary>Un subtotal de impuesto o de retención.</summary>
public sealed record TaxSubtotal
{
    /// <summary>Código del esquema tributario de la DIAN (01 IVA, 04 INC, 22 INC bolsas, 05 ReteIVA, ...).</summary>
    public required string SchemeCode { get; init; }

    public required string SchemeName { get; init; }

    public required decimal TaxableAmount { get; init; }

    public required decimal TaxAmount { get; init; }

    /// <summary>Solo se escribe cuando está presente.</summary>
    public decimal? Percent { get; init; }

    /// <summary>Se asigna para impuestos de valor fijo (por unidad), como el impuesto a las bolsas plásticas.</summary>
    public FixedValueTax? FixedValue { get; init; }
}

/// <summary>Datos por unidad de un impuesto de valor fijo.</summary>
public sealed record FixedValueTax(string UnitCode, decimal BaseUnitMeasure, decimal PerUnitAmount);

/// <summary>Valores de cac:LegalMonetaryTotal. Los totales opcionales solo se escriben cuando están presentes.</summary>
public sealed record MonetaryTotals
{
    public required decimal LineExtensionAmount { get; init; }

    public required decimal TaxExclusiveAmount { get; init; }

    public required decimal TaxInclusiveAmount { get; init; }

    public decimal? AllowanceTotalAmount { get; init; }

    public decimal? ChargeTotalAmount { get; init; }

    public decimal? PrepaidAmount { get; init; }

    public required decimal PayableAmount { get; init; }
}

/// <summary>Una línea de la factura o de una nota crédito o débito.</summary>
public sealed record InvoiceLine
{
    public required string UnitCode { get; init; }

    public required decimal Quantity { get; init; }

    public required decimal LineExtensionAmount { get; init; }

    /// <summary>Una línea gratuita también lleva su precio de referencia en cac:PricingReference.</summary>
    public required bool IsFreeOfCharge { get; init; }

    public required string Description { get; init; }

    public required string ItemCode { get; init; }

    public required ItemIdentificationScheme ItemScheme { get; init; }

    public required decimal PriceAmount { get; init; }

    public required decimal BaseQuantity { get; init; }

    /// <summary>
    /// Código del tipo de precio de referencia (01 valor comercial, ...). Solo lo usan las notas, que lo
    /// escriben en cbc:PriceTypeCode de las líneas gratuitas; la factura no lo escribe.
    /// </summary>
    public string? ReferencePriceCode { get; init; }

    public IReadOnlyList<AllowanceCharge> AllowanceCharges { get; init; } = [];

    public IReadOnlyList<TaxSubtotal> TaxSubtotals { get; init; } = [];
}

/// <summary>Esquema de identificación estándar del artículo (999 = estándar propio del contribuyente).</summary>
public sealed record ItemIdentificationScheme(string Code, string Name, string AgencyId);
