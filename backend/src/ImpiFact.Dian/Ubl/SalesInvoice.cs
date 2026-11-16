using ImpiFact.Dian.Cufe;

namespace ImpiFact.Dian.Ubl;

/// <summary>
/// Input of <see cref="InvoiceUblBuilder"/>: a national sales invoice (DIAN type 01) with every
/// catalog value already resolved to the code and name written in the XML (ADR-26). Amounts are
/// raw <see cref="decimal"/> values; the builder rounds them with DianRounding when writing.
/// </summary>
public sealed record SalesInvoice
{
    public required DianEnvironment Environment { get; init; }

    /// <summary>Operation type code written in cbc:CustomizationID (10 = standard).</summary>
    public required string OperationTypeCode { get; init; }

    /// <summary>Document type code written in cbc:InvoiceTypeCode (01 = national sales invoice).</summary>
    public required string InvoiceTypeCode { get; init; }

    public required NumberingResolution Resolution { get; init; }

    /// <summary>Consecutive number; the document ID is the resolution prefix followed by it.</summary>
    public required long Number { get; init; }

    public required DateOnly IssueDate { get; init; }

    /// <summary>Local Colombian time; the XML always carries the -05:00 offset.</summary>
    public required TimeOnly IssueTime { get; init; }

    public required SoftwareCredentials Software { get; init; }

    /// <summary>Currency of the issuing company (COP). Lines, taxes and totals use it.</summary>
    public required string LocalCurrencyCode { get; init; }

    /// <summary>Set when the invoice is issued in another currency.</summary>
    public ForeignCurrency? ForeignCurrency { get; init; }

    public required InvoiceParty Supplier { get; init; }

    public required InvoiceParty Customer { get; init; }

    public required PaymentTerms Payment { get; init; }

    /// <summary>Document-level allowances and charges.</summary>
    public IReadOnlyList<AllowanceCharge> AllowanceCharges { get; init; } = [];

    /// <summary>Tax subtotals; the builder groups them by tax scheme into cac:TaxTotal.</summary>
    public IReadOnlyList<TaxSubtotal> TaxSubtotals { get; init; } = [];

    /// <summary>Withholding subtotals; grouped by tax scheme into cac:WithholdingTaxTotal.</summary>
    public IReadOnlyList<TaxSubtotal> WithholdingSubtotals { get; init; } = [];

    public required MonetaryTotals Totals { get; init; }

    public required IReadOnlyList<InvoiceLine> Lines { get; init; }

    /// <summary>Document number as written in cbc:ID.</summary>
    public string DocumentNumber => Resolution.Prefix + Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>DIAN numbering resolution that authorizes the invoice number.</summary>
public sealed record NumberingResolution(
    string AuthorizationNumber,
    DateOnly StartDate,
    DateOnly EndDate,
    string Prefix,
    long From,
    long To,
    string TechnicalKey);

/// <summary>Software registered with the DIAN; enters sts:SoftwareID and sts:SoftwareSecurityCode.</summary>
public sealed record SoftwareCredentials(string SoftwareId, string Pin);

/// <summary>
/// Foreign document currency. cac:PaymentExchangeRate is written only when both the rate and its
/// date are known.
/// </summary>
public sealed record ForeignCurrency(string CurrencyCode, decimal? CalculationRate, DateOnly? CalculationRateDate);

/// <summary>Supplier or customer.</summary>
public sealed record InvoiceParty
{
    /// <summary>Organization type code written in cbc:AdditionalAccountID (1 legal entity, 2 natural person).</summary>
    public required string OrganizationTypeCode { get; init; }

    public required string IdentificationNumber { get; init; }

    /// <summary>Verification digit; written as an empty schemeID when absent.</summary>
    public string? CheckDigit { get; init; }

    /// <summary>Identification type code (31 NIT, 13 citizenship card, ...).</summary>
    public required string IdentificationTypeCode { get; init; }

    public required string Name { get; init; }

    /// <summary>When null the physical location and the registration address are omitted.</summary>
    public PartyAddress? Address { get; init; }

    /// <summary>Regime code written as the listName of cbc:TaxLevelCode (48, 49).</summary>
    public required string RegimeCode { get; init; }

    /// <summary>Fiscal liability code written in cbc:TaxLevelCode (O-23, R-99-PN, ...).</summary>
    public required string LiabilityCode { get; init; }

    public required string TaxSchemeCode { get; init; }

    public required string TaxSchemeName { get; init; }

    /// <summary>Merchant registration number; may be empty.</summary>
    public string MerchantRegistration { get; init; } = "";

    public string? Telephone { get; init; }

    public string? Email { get; init; }

    /// <summary>The DIAN final consumer (222222222222) carries no contact data.</summary>
    public bool IsFinalConsumer => IdentificationNumber == "222222222222";
}

/// <summary>
/// Address of a party. <see cref="CityName"/> is kept as stored in the catalog: the builder trims it
/// everywhere except in the supplier's physical location, where ApiDian writes it untrimmed.
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

/// <summary>Payment form (cash/credit) and method.</summary>
public sealed record PaymentTerms(string FormCode, string MethodCode, DateOnly? DueDate, int? DurationDays);

/// <summary>A discount (allowance) or a charge, at document or line level.</summary>
public sealed record AllowanceCharge
{
    public required bool IsCharge { get; init; }

    /// <summary>DIAN discount reason code; written only for allowances.</summary>
    public string? ReasonCode { get; init; }

    public required string Reason { get; init; }

    /// <summary>Percentage; when null the builder uses Amount / BaseAmount * 100 (0 when there is no base).</summary>
    public decimal? MultiplierFactorNumeric { get; init; }

    public required decimal Amount { get; init; }

    /// <summary>Written only when present.</summary>
    public decimal? BaseAmount { get; init; }
}

/// <summary>One tax or withholding subtotal.</summary>
public sealed record TaxSubtotal
{
    /// <summary>DIAN tax scheme code (01 IVA, 04 INC, 22 INC bags, 05 ReteIVA, ...).</summary>
    public required string SchemeCode { get; init; }

    public required string SchemeName { get; init; }

    public required decimal TaxableAmount { get; init; }

    public required decimal TaxAmount { get; init; }

    /// <summary>Written only when present.</summary>
    public decimal? Percent { get; init; }

    /// <summary>Set for fixed-value taxes (per unit), such as the plastic bag tax.</summary>
    public FixedValueTax? FixedValue { get; init; }
}

/// <summary>Per-unit data of a fixed-value tax.</summary>
public sealed record FixedValueTax(string UnitCode, decimal BaseUnitMeasure, decimal PerUnitAmount);

/// <summary>Values of cac:LegalMonetaryTotal. Optional totals are written only when present.</summary>
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

/// <summary>An invoice line.</summary>
public sealed record InvoiceLine
{
    public required string UnitCode { get; init; }

    public required decimal Quantity { get; init; }

    public required decimal LineExtensionAmount { get; init; }

    /// <summary>A free line also carries its reference price in cac:PricingReference.</summary>
    public required bool IsFreeOfCharge { get; init; }

    public required string Description { get; init; }

    public required string ItemCode { get; init; }

    public required ItemIdentificationScheme ItemScheme { get; init; }

    public required decimal PriceAmount { get; init; }

    public required decimal BaseQuantity { get; init; }

    public IReadOnlyList<AllowanceCharge> AllowanceCharges { get; init; } = [];

    public IReadOnlyList<TaxSubtotal> TaxSubtotals { get; init; } = [];
}

/// <summary>Standard item identification scheme (999 = taxpayer's own standard).</summary>
public sealed record ItemIdentificationScheme(string Code, string Name, string AgencyId);
