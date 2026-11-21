using System.Globalization;
using System.Text.Json;
using ImpiFact.Dian.Cufe;
using ImpiFact.Dian.Rounding;
using ImpiFact.Dian.Ubl;
using static ImpiFact.Golden.Tests.ApiDianJson;

namespace ImpiFact.Golden.Tests;

/// <summary>Invoice variants on template 01, with the ApiDian <c>type_operation_id</c> each controller uses by default.</summary>
public enum ApiDianInvoiceVariant
{
    /// <summary>InvoiceAIUController: operation 9 (code 09).</summary>
    Aiu = 9,

    /// <summary>InvoiceMandateController: operation 11 (code 11).</summary>
    Mandate = 11,

    /// <summary>InvoiceTransportController: operation 21 (code 12).</summary>
    Transport = 21,

    /// <summary>InvoiceExportController: template 02, operation 10 (code 10).</summary>
    Export = 10,
}

/// <summary>
/// Convierte una solicitud de factura de ApiDian (el input.json de un caso de referencia, cuyo XML y CUFE
/// generó el PHP original) más la configuración fija de captura al modelo <see cref="SalesInvoice"/> de
/// ImpiFact.Dian, traduciendo los ID de catálogo con <see cref="ApiDianCatalog"/>. Un valor opcional solo
/// se pasa cuando ApiDian lo habría escrito en el XML, según cómo PHP decide si un valor cuenta como vacío
/// (ver <see cref="ApiDianJson.TruthyDecimal"/>). Así el constructor recibe exactamente lo que escribió
/// ApiDian. <see cref="ApiDianNoteAdapter"/> reutiliza las conversiones de partes, pagos, impuestos, totales
/// y líneas, que son las mismas en las notas.
/// </summary>
public sealed class ApiDianInvoiceAdapter(ApiDianCatalog catalog)
{
    /// <summary>
    /// ID de impuestos de ApiDian que se cobran como un valor fijo por unidad y no como porcentaje
    /// (app/TaxTotal.php, is_fixed_value).
    /// </summary>
    private static readonly int[] FixedValueTaxIds = [2, 10, 19, 20, 21, 23];

    public SalesInvoice Map(JsonElement request)
    {
        var localCurrency = catalog.Get("currencies", GoldenCaptureContext.CompanyCurrencyId).Code;
        return new SalesInvoice
        {
            Environment = (DianEnvironment)GoldenCaptureContext.EnvironmentId,
            QrCodeBaseUrl = GoldenCaptureContext.QrCodeBaseUrl,
            OperationTypeCode = catalog.Get("operationTypes", GoldenCaptureContext.OperationTypeId).Code,
            InvoiceTypeCode = catalog.Get("documentTypes", RequiredInt(request, "type_document_id")).Code,
            Resolution = new NumberingResolution(
                GoldenCaptureContext.ResolutionNumber,
                GoldenCaptureContext.ResolutionDateFrom,
                GoldenCaptureContext.ResolutionDateTo,
                RequiredText(request, "prefix"),
                GoldenCaptureContext.ResolutionFrom,
                GoldenCaptureContext.ResolutionTo,
                GoldenCaptureContext.TechnicalKey),
            Number = long.Parse(RequiredText(request, "number"), CultureInfo.InvariantCulture),
            IssueDate = DateOnly.ParseExact(RequiredText(request, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            IssueTime = TimeOnly.ParseExact(RequiredText(request, "time"), "HH:mm:ss", CultureInfo.InvariantCulture),
            Software = new SoftwareCredentials(GoldenCaptureContext.SoftwareId, GoldenCaptureContext.SoftwarePin),
            LocalCurrencyCode = localCurrency,
            ForeignCurrency = MapForeignCurrency(request),
            Supplier = MapSupplier(),
            Customer = MapCustomer(request.GetProperty("customer")),
            Payments = MapPayments(request.GetProperty("payment_form")),
            AllowanceCharges = MapList(request, "allowance_charges", MapAllowanceCharge),
            TaxSubtotals = MapList(request, "tax_totals", MapTaxSubtotal),
            WithholdingSubtotals = MapList(request, "with_holding_tax_total", MapTaxSubtotal),
            Note = OptionalText(request, "notes"),
            Totals = MapTotals(request.GetProperty("legal_monetary_totals")),
            Lines = MapList(request, "invoice_lines", MapLine),
        };
    }

    /// <summary>
    /// An AIU, mandate or freight transport invoice: the same template 01 as <see cref="Map"/>, with the default operation
    /// and the flags that ApiDian's InvoiceAIUController, InvoiceMandateController and InvoiceTransportController set
    /// (php-rules, section 10). The golden inputs never send <c>type_operation_id</c>.
    /// </summary>
    public SalesInvoice MapVariant(JsonElement request, ApiDianInvoiceVariant variant)
    {
        var invoice = Map(request);
        var lines = request.GetProperty("invoice_lines").EnumerateArray().ToList();
        return invoice with
        {
            OperationTypeCode = catalog.Get("operationTypes", (int)variant).Code,

            // php-rules 11, item 4: InvoiceExportController never passes idcurrency nor the rate to template 02.
            ForeignCurrency = variant == ApiDianInvoiceVariant.Export ? null : invoice.ForeignCurrency,
            AiuContractObject = variant == ApiDianInvoiceVariant.Aiu ? RequiredText(request, "noteAIU") : null,
            LineIdScheme = variant switch
            {
                ApiDianInvoiceVariant.Mandate => InvoiceLineIdScheme.Mandate,
                ApiDianInvoiceVariant.Transport => InvoiceLineIdScheme.Transport,
                _ => InvoiceLineIdScheme.Plain,
            },
            Export = variant == ApiDianInvoiceVariant.Export ? MapExport(request) : null,
            Lines = invoice.Lines.Select((line, index) => line with
            {
                Mandator = variant == ApiDianInvoiceVariant.Mandate ? MapMandator(lines[index]) : null,
                Consignment = variant == ApiDianInvoiceVariant.Transport ? MapConsignment(lines[index]) : null,
                BrandName = variant == ApiDianInvoiceVariant.Export ? OptionalText(lines[index], "brandname") : null,
                ModelName = variant == ApiDianInvoiceVariant.Export ? OptionalText(lines[index], "modelname") : null,
            }).ToList(),
        };
    }

    /// <summary>deliveryterms and, when sent, the k_supplement of InvoiceExportController (php-rules 11).</summary>
    private static ExportTerms MapExport(JsonElement request)
    {
        var terms = request.GetProperty("deliveryterms");
        ExportSupplement? supplement = null;
        if (request.TryGetProperty("k_supplement", out var k) && k.ValueKind == JsonValueKind.Object)
        {
            supplement = new ExportSupplement(
                ExportSupplement.AdditionalInformationFields.Select(field => RequiredText(k, field.Key)).ToList(),
                ExportSupplement.CopTotalFields.Select(field => RequiredText(k, field)).ToList());
        }

        return new ExportTerms(
            new DeliveryTerms(
                RequiredText(terms, "special_terms"), RequiredText(terms, "loss_risk_responsibility_code"), RequiredText(terms, "loss_risk")),
            supplement);
    }

    private static LineMandator? MapMandator(JsonElement line) =>
        OptionalText(line, "agentparty") is { } nit ? new LineMandator(nit, OptionalText(line, "agentparty_dv") ?? "") : null;

    private RndcConsignment? MapConsignment(JsonElement line) =>
        line.GetProperty("is_RNDC").GetBoolean()
            ? new RndcConsignment(
                RequiredText(line, "RNDC_consignment_number"),
                RequiredText(line, "internal_consignment_number"),
                RequiredText(line, "value_consignment"),
                catalog.Get("unitMeasures", RequiredInt(line, "unit_measure_consignment_id")).Code,
                RequiredText(line, "quantity_consignment"))
            : null;

    internal ForeignCurrency? MapForeignCurrency(JsonElement request)
    {
        var currencyId = OptionalText(request, "idcurrency");
        if (currencyId is null)
        {
            return null;
        }

        var rate = OptionalText(request, "calculationrate");
        var date = OptionalText(request, "calculationratedate");
        return new ForeignCurrency(
            catalog.Get("currencies", currencyId).Code,
            rate is null ? null : DianRounding.Parse(rate),
            date is null ? null : DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    internal InvoiceParty MapSupplier() => new()
    {
        OrganizationTypeCode = catalog.Get("organizationTypes", GoldenCaptureContext.CompanyOrganizationTypeId).Code,
        IdentificationNumber = GoldenCaptureContext.CompanyNit,
        CheckDigit = GoldenCaptureContext.CompanyDv,
        IdentificationTypeCode = catalog.Get("identificationTypes", GoldenCaptureContext.CompanyIdentificationTypeId).Code,
        Name = GoldenCaptureContext.CompanyName,
        Address = MapAddress(GoldenCaptureContext.CompanyMunicipalityId.ToString(CultureInfo.InvariantCulture), GoldenCaptureContext.CompanyAddress),
        RegimeCode = catalog.Get("regimes", GoldenCaptureContext.CompanyRegimeId).Code,
        LiabilityCode = catalog.Get("liabilities", GoldenCaptureContext.CompanyLiabilityId).Code,
        TaxSchemeCode = catalog.Get("taxes", GoldenCaptureContext.PartyTaxId).Code,
        TaxSchemeName = catalog.Get("taxes", GoldenCaptureContext.PartyTaxId).Name,
        MerchantRegistration = GoldenCaptureContext.CompanyMerchantRegistration,
        Telephone = GoldenCaptureContext.CompanyPhone,
        Email = GoldenCaptureContext.CompanyEmail,
    };

    internal InvoiceParty MapCustomer(JsonElement customer)
    {
        var municipalityId = OptionalText(customer, "municipality_id");
        var address = OptionalText(customer, "address");
        var countryId = OptionalText(customer, "country_id");
        var foreign = countryId is not null && countryId != GoldenCaptureContext.CountryId.ToString(CultureInfo.InvariantCulture);
        return new InvoiceParty
        {
            OrganizationTypeCode = catalog.Get("organizationTypes", RequiredInt(customer, "type_organization_id")).Code,
            IdentificationNumber = RequiredText(customer, "identification_number"),
            CheckDigit = OptionalText(customer, "dv"),
            IdentificationTypeCode = catalog.Get("identificationTypes", RequiredInt(customer, "type_document_identification_id")).Code,
            Name = RequiredText(customer, "name"),

            // ApiDian solo escribe los bloques de dirección del cliente cuando existen la dirección y el municipio. Outside
            // Colombia, _accounting.blade.php writes the fixed codes 00001 and 01 with municipality_name and state_name.
            Address = string.IsNullOrEmpty(address) ? null
                : foreign ? MapForeignAddress(customer, countryId!, address)
                : municipalityId is null ? null : MapAddress(municipalityId, address),
            RegimeCode = catalog.Get("regimes", RequiredInt(customer, "type_regime_id")).Code,
            LiabilityCode = catalog.Get("liabilities", RequiredInt(customer, "type_liability_id")).Code,
            TaxSchemeCode = catalog.Get("taxes", GoldenCaptureContext.PartyTaxId).Code,
            TaxSchemeName = catalog.Get("taxes", GoldenCaptureContext.PartyTaxId).Name,
            MerchantRegistration = OptionalText(customer, "merchant_registration") ?? "",
            Telephone = OptionalText(customer, "phone"),
            Email = OptionalText(customer, "email"),
        };
    }

    private PartyAddress MapAddress(string municipalityId, string addressLine)
    {
        var municipality = catalog.Get("municipalities", municipalityId);
        var department = catalog.Get("departments", municipality.DepartmentId
            ?? throw new InvalidDataException($"Municipality {municipalityId} has no department"));
        var country = catalog.Get("countries", GoldenCaptureContext.CountryId);
        var language = catalog.Get("languages", GoldenCaptureContext.LanguageId);
        return new PartyAddress(
            municipality.Code, municipality.Name, department.Name, department.Code,
            addressLine, country.Code, country.Name, language.Code);
    }

    private PartyAddress MapForeignAddress(JsonElement customer, string countryId, string addressLine)
    {
        var country = catalog.Get("countries", countryId);
        var language = catalog.Get("languages", GoldenCaptureContext.LanguageId);
        return new PartyAddress(
            "00001", OptionalText(customer, "municipality_name") ?? "", OptionalText(customer, "state_name") ?? "", "01",
            addressLine, country.Code, country.Name, language.Code);
    }

    /// <summary>Como el PHP: <c>payment_form</c> es un objeto suelto o una lista de formas de pago.</summary>
    internal IReadOnlyList<PaymentTerms> MapPayments(JsonElement payment) =>
        payment.ValueKind == JsonValueKind.Array ? payment.EnumerateArray().Select(MapPayment).ToList() : [MapPayment(payment)];

    internal PaymentTerms MapPayment(JsonElement payment)
    {
        var dueDate = OptionalText(payment, "payment_due_date");
        var duration = OptionalText(payment, "duration_measure");
        return new PaymentTerms(
            catalog.Get("paymentForms", RequiredInt(payment, "payment_form_id")).Code,
            catalog.Get("paymentMethods", RequiredInt(payment, "payment_method_id")).Code,
            dueDate is null ? null : DateOnly.ParseExact(dueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            duration is null ? null : int.Parse(duration, CultureInfo.InvariantCulture));
    }

    internal AllowanceCharge MapAllowanceCharge(JsonElement item)
    {
        var isCharge = item.GetProperty("charge_indicator").GetBoolean();
        var discountId = OptionalText(item, "discount_id");
        var multiplier = OptionalText(item, "multiplier_factor_numeric");
        return new AllowanceCharge
        {
            IsCharge = isCharge,

            // ApiDian solo escribe el código de motivo de descuento para los descuentos.
            ReasonCode = isCharge || discountId is null ? null : catalog.Get("discounts", discountId).Code,
            Reason = RequiredText(item, "allowance_charge_reason"),
            MultiplierFactorNumeric = multiplier is null ? null : DianRounding.Parse(multiplier),
            Amount = RequiredDecimal(item, "amount"),
            BaseAmount = TruthyDecimal(item, "base_amount"),
        };
    }

    internal TaxSubtotal MapTaxSubtotal(JsonElement item)
    {
        var taxId = RequiredInt(item, "tax_id");
        var tax = catalog.Get("taxes", taxId);
        var percent = OptionalText(item, "percent");
        FixedValueTax? fixedValue = null;
        if (FixedValueTaxIds.Contains(taxId) && TruthyDecimal(item, "base_unit_measure") is { } baseUnitMeasure)
        {
            fixedValue = new FixedValueTax(
                catalog.Get("unitMeasures", RequiredInt(item, "unit_measure_id")).Code,
                baseUnitMeasure,
                RequiredDecimal(item, "per_unit_amount"));
        }

        return new TaxSubtotal
        {
            SchemeCode = tax.Code,
            SchemeName = tax.Name,
            TaxableAmount = RequiredDecimal(item, "taxable_amount"),
            TaxAmount = RequiredDecimal(item, "tax_amount"),
            Percent = string.IsNullOrEmpty(percent) ? null : DianRounding.Parse(percent),
            FixedValue = fixedValue,
        };
    }

    internal static MonetaryTotals MapTotals(JsonElement totals) => new()
    {
        LineExtensionAmount = RequiredDecimal(totals, "line_extension_amount"),
        TaxExclusiveAmount = RequiredDecimal(totals, "tax_exclusive_amount"),
        TaxInclusiveAmount = RequiredDecimal(totals, "tax_inclusive_amount"),
        AllowanceTotalAmount = TruthyDecimal(totals, "allowance_total_amount"),
        ChargeTotalAmount = TruthyDecimal(totals, "charge_total_amount"),
        PrepaidAmount = TruthyDecimal(totals, "pre_paid_amount"),
        PayableAmount = RequiredDecimal(totals, "payable_amount"),
    };

    internal InvoiceLine MapLine(JsonElement line)
    {
        var itemType = catalog.Get("itemIdentificationTypes", RequiredInt(line, "type_item_identification_id"));

        // La factura no escribe el tipo de precio de referencia; las notas sí, en las líneas gratuitas.
        var referencePriceId = OptionalText(line, "reference_price_id");
        return new InvoiceLine
        {
            UnitCode = catalog.Get("unitMeasures", RequiredInt(line, "unit_measure_id")).Code,
            Quantity = RequiredDecimal(line, "invoiced_quantity"),
            LineExtensionAmount = RequiredDecimal(line, "line_extension_amount"),
            IsFreeOfCharge = line.GetProperty("free_of_charge_indicator").GetBoolean(),
            Description = RequiredText(line, "description"),
            ItemCode = RequiredText(line, "code"),
            ItemScheme = new ItemIdentificationScheme(itemType.Code, itemType.Name, itemType.CodeAgency ?? ""),
            PriceAmount = RequiredDecimal(line, "price_amount"),
            BaseQuantity = RequiredDecimal(line, "base_quantity"),
            ReferencePriceCode = referencePriceId is null ? null : catalog.Get("referencePrices", referencePriceId).Code,
            AllowanceCharges = MapList(line, "allowance_charges", MapAllowanceCharge),
            TaxSubtotals = MapList(line, "tax_totals", MapTaxSubtotal),
        };
    }
}
