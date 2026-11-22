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

    /// <summary>
    /// Formas de pago, al menos una: cada una es un cac:PaymentMeans (con cbc:PaymentID desde 0) y cac:PaymentTerms
    /// usa la primera, como ApiDian.
    /// </summary>
    public required IReadOnlyList<PaymentTerms> Payments { get; init; }

    /// <summary>
    /// Anticipos ya recibidos; cada uno es un cac:PrepaidPayment (grupo FBD del anexo técnico de la DIAN). Su suma va en
    /// <see cref="MonetaryTotals.PrepaidAmount"/> y no cambia el valor a pagar ni el CUFE.
    /// </summary>
    public IReadOnlyList<PrepaidPayment> PrepaidPayments { get; init; } = [];

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

    /// <summary>
    /// Object of the AIU contract (<c>noteAIU</c>) of an AIU invoice (administration, contingencies and profit); null
    /// otherwise. Like ApiDian (<c>_invoice_lines.blade.php</c>), the builder writes «Contrato de servicios AIU por
    /// concepto de: {object}» as the first cbc:Note of every line whose description is exactly «Administración».
    /// </summary>
    public string? AiuContractObject { get; init; }

    /// <summary>How the cbc:ID of each line carries a schemeID: none, mandate or freight transport.</summary>
    public InvoiceLineIdScheme LineIdScheme { get; init; } = InvoiceLineIdScheme.Plain;

    /// <summary>
    /// Export invoice data (type 02, ApiDian template <c>02.blade.php</c>); null for the other invoices. With it the
    /// builder writes cac:DeliveryTerms after the customer, omits cac:PaymentTerms and, when the supplement is present,
    /// adds the third ext:UBLExtension (annex 1.9, supplement K).
    /// </summary>
    public ExportTerms? Export { get; init; }

    /// <summary>
    /// When true every @currencyID is the document currency (cbc:DocumentCurrencyCode), as annex 1.9 rule FAD15b asks
    /// (only the TotalesCop extension is in pesos). When false, the lines, charges, taxes and totals keep the local
    /// currency like ApiDian's template 01 (php-rules 3.2, item 6), which the golden cases pin.
    /// </summary>
    public bool AmountsInDocumentCurrency { get; init; }

    /// <summary>
    /// Support document data (type 05, ApiDian template <c>05.blade.php</c>); null for the sales invoices. With it the
    /// builder writes the support-document profile and the CUDS (with <see cref="Software"/>'s PIN; the technical key of
    /// the resolution is never used), lays the parties out as that template does (<see cref="Supplier"/> is the seller not
    /// obliged to invoice and <see cref="Customer"/> the issuing company, which is also the software provider), always
    /// writes cac:PaymentExchangeRate and gives each line its cac:InvoicePeriod without cbc:FreeOfChargeIndicator.
    /// </summary>
    public SupportDocumentTerms? SupportDocument { get; init; }

    /// <summary>
    /// The paper invoice that a contingency invoice type 03 transcribes (ApiDian template <c>03.blade.php</c>); null for the
    /// other invoices. With it the builder writes cac:AdditionalDocumentReference right after cbc:LineCountNumeric and the
    /// CUDE with <see cref="Software"/>'s PIN (schemeName <c>CUDE-SHA384</c>): annex 1.9, section 11.1.4, Nota-2, says
    /// the transcription of a contingency invoice never uses a technical key, because its numbering has none.
    /// </summary>
    public PaperInvoiceReference? PaperInvoice { get; init; }

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

/// <summary>
/// What a support document adds to the sales invoice. Without <see cref="SalesInvoice.ForeignCurrency"/>, ApiDian writes
/// cac:PaymentExchangeRate anyway, from the company currency to itself with rate 1 and <see cref="ExchangeRateDate"/>
/// (the day of the issue request, php-rules 13.2, item 4).
/// </summary>
public sealed record SupportDocumentTerms(DateOnly ExchangeRateDate);

/// <summary>
/// The paper (or manual) invoice behind a contingency invoice type 03: its number (cbc:ID), date (cbc:IssueDate) and
/// document type code (cbc:DocumentTypeCode, for example <c>01</c>). Annex 1.9 rule FAI01 requires the group for type 03.
/// </summary>
public sealed record PaperInvoiceReference(string Id, DateOnly IssueDate, string DocumentTypeCode);

/// <summary>
/// cac:InvoicePeriod of a support-document line: when the purchase was made (cbc:StartDate) and how it is reported
/// (<c>type_generation_transmitions</c>: 1 per operation, 2 weekly accumulated) as code and name.
/// </summary>
public sealed record LineGenerationPeriod(DateOnly StartDate, string DescriptionCode, string Description);

/// <summary>What an export invoice adds to the sales invoice: its Incoterm and, optionally, the supplement K.</summary>
public sealed record ExportTerms(DeliveryTerms DeliveryTerms, ExportSupplement? Supplement);

/// <summary>
/// cac:DeliveryTerms of an export invoice: the payment of the transport costs (cbc:SpecialTerms), the Incoterm
/// (cbc:LossRiskResponsibilityCode, annex 1.9 list 13.3.7) and cbc:LossRisk, a free text the DIAN does not use.
/// </summary>
public sealed record DeliveryTerms(string SpecialTerms, string LossRiskResponsibilityCode, string LossRisk);

/// <summary>
/// Supplement K of annex 1.9 (section 21): the CustomTagGeneral extension with the export data
/// (Interoperabilidad) and the totals converted to pesos (TotalesCop). The DIAN does not validate it; it feeds the
/// graphic representation. Values are written as they arrive, like ApiDian's <c>_ubl_extensions.blade.php</c>.
/// </summary>
/// <param name="AdditionalInformation">One value per <see cref="AdditionalInformationFields"/>, in that order.</param>
/// <param name="CopTotals">One value per <see cref="CopTotalFields"/>, in that order.</param>
public sealed record ExportSupplement(IReadOnlyList<string> AdditionalInformation, IReadOnlyList<string> CopTotals)
{
    /// <summary>Key of each value in the <c>k_supplement</c> request object and the name ApiDian writes for it.</summary>
    public static readonly IReadOnlyList<(string Key, string Name)> AdditionalInformationFields =
    [
        ("responsible_incharge", "Responsable/Encargado"),
        ("departure_place", "Lugar de Salida"),
        ("conveyance", "Medio de transporte"),
        ("transport_document_type", "Tipo de Doc.de transporte"),
        ("transport_document_number", "N° de Doc. de transporte"),
        ("transporter_processor", "Transportadora o Tramitadora"),
        ("merchandise_origin_country", "País de Origen de la M/cia"),
        ("destination", "Destino"),
        ("payment_means", "Términos de pago"),
        ("insurance_carrier", "Seguro"),
        ("observations", "Observaciones"),
    ];

    /// <summary>Elements of TotalesCop; each one is also its key in the <c>k_supplement</c> request object.</summary>
    public static readonly IReadOnlyList<string> CopTotalFields =
    [
        "FctConvCop", "MonedaCop", "SubTotalCop", "DescuentoDetalleCop", "RecargoDetalleCop", "TotalBrutoFacturaCop",
        "TotIvaCop", "TotIncCop", "TotBolCop", "ImpOtroCop", "MntImpCop", "TotalNetoFacturaCop", "MntDctoCop",
        "MntRcgoCop", "VlrPagarCop", "ReteFueCop", "ReteIvaCop", "ReteIcaCop", "TotAnticiposCop",
    ];
}

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

    /// <summary>
    /// Postal code written as cbc:PostalZone of the physical location; only the seller of a support document has it
    /// (<c>postal_zone_code</c>). Null for every other party.
    /// </summary>
    public string? PostalZone { get; init; }

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

/// <summary>Forma de pago (contado/crédito) y medio de pago de una entrada de cac:PaymentMeans.</summary>
public sealed record PaymentTerms(string FormCode, string MethodCode, DateOnly? DueDate, int? DurationDays);

/// <summary>
/// Un anticipo (cac:PrepaidPayment). <see cref="PaidDate"/> e <see cref="InstructionId"/> son opcionales en el anexo
/// (reglas FBD06 y FBD08) y solo se escriben cuando llegan.
/// </summary>
/// <param name="Id">Identificación del pago del anticipo (cbc:ID, regla FBD02).</param>
/// <param name="PaidAmount">Valor pagado por anticipado, sin redondear (cbc:PaidAmount).</param>
/// <param name="ReceivedDate">Fecha en que se recibió el pago (cbc:ReceivedDate).</param>
/// <param name="PaidDate">Fecha en que se hizo el pago (cbc:PaidDate).</param>
/// <param name="InstructionId">Instrucciones relativas al pago (cbc:InstructionID).</param>
public sealed record PrepaidPayment(string Id, decimal PaidAmount, DateOnly ReceivedDate, DateOnly? PaidDate, string? InstructionId);

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

    /// <summary>
    /// Free note of the line (<c>notes</c>), written by the invoice builder as a cbc:Note after the AIU note, like ApiDian's
    /// <c>_invoice_lines.blade.php</c>; null when not sent. The credit and debit notes do not write it.
    /// </summary>
    public string? Note { get; init; }

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

    /// <summary>
    /// Brand of the item (cbc:BrandName after cbc:Description); null when not sent. The invoice and the support-document
    /// adjustment note write it; the credit and debit notes do not.
    /// </summary>
    public string? BrandName { get; init; }

    /// <summary>Model of the item (cbc:ModelName after cbc:BrandName); null when not sent. Written like <see cref="BrandName"/>.</summary>
    public string? ModelName { get; init; }

    /// <summary>Support-document line only: its cac:InvoicePeriod. Only <see cref="InvoiceUblBuilder"/> uses it.</summary>
    public LineGenerationPeriod? GenerationPeriod { get; init; }

    /// <summary>
    /// Third party on whose behalf the line is sold (mandate invoice); written in cac:Item as
    /// cac:InformationContentProviderParty/cac:PowerOfAttorney/cac:AgentParty. Only <see cref="InvoiceUblBuilder"/> uses it.
    /// </summary>
    public LineMandator? Mandator { get; init; }

    /// <summary>
    /// RNDC consignment of a freight transport line; written in cac:Item as the cac:AdditionalItemProperty 01, 02 and 03.
    /// Only <see cref="InvoiceUblBuilder"/> uses it.
    /// </summary>
    public RndcConsignment? Consignment { get; init; }
}

/// <summary>The schemeID that ApiDian writes on the cbc:ID of each invoice line (<c>_invoice_lines.blade.php</c>).</summary>
public enum InvoiceLineIdScheme
{
    /// <summary>No schemeID (standard and AIU invoices).</summary>
    Plain,

    /// <summary>Mandate invoice: every line carries schemeID="1".</summary>
    Mandate,

    /// <summary>Freight transport invoice: schemeID="1" on lines with an RNDC consignment, "0" on the others.</summary>
    Transport,
}

/// <summary>Mandator of a line: NIT (cbc:ID, schemeName 31) and its check digit (schemeID).</summary>
public sealed record LineMandator(string IdentificationNumber, string CheckDigit);

/// <summary>
/// RNDC (Registro Nacional de Despachos de Carga) data of a freight transport line. The values are written as they
/// arrive, without number formatting, like ApiDian.
/// </summary>
/// <param name="RndcConsignmentNumber">Consignment number in the RNDC (property 01).</param>
/// <param name="InternalConsignmentNumber">Internal consignment number of the carrier (property 02).</param>
/// <param name="Value">Consignment value (property 03, cbc:Value).</param>
/// <param name="UnitCode">Unit of the transported quantity (property 03, unitCode of cbc:ValueQuantity).</param>
/// <param name="Quantity">Transported quantity (property 03, cbc:ValueQuantity).</param>
public sealed record RndcConsignment(string RndcConsignmentNumber, string InternalConsignmentNumber, string Value, string UnitCode, string Quantity);

/// <summary>Esquema de identificación estándar del artículo (999 = estándar propio del contribuyente).</summary>
public sealed record ItemIdentificationScheme(string Code, string Name, string AgencyId);
