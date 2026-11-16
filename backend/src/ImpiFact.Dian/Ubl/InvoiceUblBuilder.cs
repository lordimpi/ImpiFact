using System.Globalization;
using ImpiFact.Dian.Cufe;
using ImpiFact.Dian.Rounding;

namespace ImpiFact.Dian.Ubl;

/// <summary>
/// Resultado de construir una factura: el XML antes de firmar y su CUFE (código único de la factura
/// electrónica: un hash que identifica la factura ante la DIAN).
/// </summary>
public sealed record InvoiceUblResult(string Xml, string Cufe);

/// <summary>
/// Construye el XML UBL 2.1 (el estándar XML de documentos comerciales que usa la DIAN) de una factura
/// electrónica de venta nacional (tipo 01), todavía sin firmar.
/// </summary>
/// <remarks>
/// La salida ya trae los campos que la implementación de referencia (ApiDian, en PHP) llena en su paso de
/// firma: cbc:UUID (el CUFE), sts:SoftwareSecurityCode y sts:QRCode. El segundo ext:ExtensionContent queda
/// vacío: es el lugar reservado para la firma. El orden de los elementos, cuáles elementos opcionales
/// aparecen y el formato siguen byte a byte los casos de referencia (golden): XML de ApiDian contra los
/// que las pruebas comparan la salida; todo número pasa por <see cref="DianRounding"/>.
/// </remarks>
public static class InvoiceUblBuilder
{
    private const string DianAgencyId = "195";
    private const string DianAgencyName = "CO, DIAN (Dirección de Impuestos y Aduanas Nacionales)";
    private const string CufeAlgorithm = "CUFE-SHA384";
    private const string AuthorizationProviderNit = "800197268";
    private const string IvaCode = "01";
    private const string IcaCode = "03";
    private const string IncCode = "04";

    // Las plantillas de ApiDian dejan saltos de línea y espacios de su propia indentación dentro del texto
    // de la línea de dirección (cbc:Line) y del precio (cbc:PriceAmount, cbc:BaseQuantity). Las fixtures
    // golden de la DIAN los conservan, así que se reproducen literalmente para igualar byte a byte.
    private static readonly string PhysicalAddressLinePrefix = "\n" + new string(' ', 60);
    private static readonly string PhysicalAddressLineSuffix = "\n" + new string(' ', 52);
    private static readonly string PriceValuePrefix = "\n" + new string(' ', 16);
    private static readonly string PriceValueSuffix = "\n" + new string(' ', 12);

    public static InvoiceUblResult Build(SalesInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        if (invoice.Supplier.Address is null)
        {
            throw new ArgumentException("The supplier must have an address.", nameof(invoice));
        }

        var cufe = ComputeCufe(invoice);
        var root = new UblElement("Invoice")
            .Attr("xmlns", "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2")
            .Attr("xmlns:cac", "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")
            .Attr("xmlns:cbc", "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")
            .Attr("xmlns:ds", "http://www.w3.org/2000/09/xmldsig#")
            .Attr("xmlns:ext", "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2")
            .Attr("xmlns:sts", "urn:dian:gov:co:facturaelectronica:Structures-2-1")
            .Attr("xmlns:xades", "http://uri.etsi.org/01903/v1.3.2#")
            .Attr("xmlns:xades141", "http://uri.etsi.org/01903/v1.4.1#")
            .Attr("xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance")
            .Attr("xsi:schemaLocation", "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2     http://docs.oasis-open.org/ubl/os-UBL-2.1/xsd/maindoc/UBL-Invoice-2.1.xsd");

        root.Add(Extensions(invoice, cufe));
        root.Add("cbc:UBLVersionID", "UBL 2.1");
        root.Add("cbc:CustomizationID", invoice.OperationTypeCode);
        root.Add("cbc:ProfileID", "DIAN 2.1: Factura Electrónica de Venta");
        root.Add("cbc:ProfileExecutionID", EnvironmentCode(invoice.Environment));
        root.Add("cbc:ID", invoice.DocumentNumber);
        root.Add(new UblElement("cbc:UUID", cufe)
            .Attr("schemeID", EnvironmentCode(invoice.Environment))
            .Attr("schemeName", CufeAlgorithm));
        root.Add("cbc:IssueDate", Date(invoice.IssueDate));
        root.Add("cbc:IssueTime", Time(invoice.IssueTime));
        root.Add("cbc:InvoiceTypeCode", invoice.InvoiceTypeCode);
        root.Add("cbc:DocumentCurrencyCode", invoice.ForeignCurrency?.CurrencyCode ?? invoice.LocalCurrencyCode);
        root.Add("cbc:LineCountNumeric", invoice.Lines.Count.ToString(CultureInfo.InvariantCulture));
        root.Add(Party("cac:AccountingSupplierParty", invoice.Supplier, isSupplier: true, invoice.Resolution.Prefix));
        root.Add(Party("cac:AccountingCustomerParty", invoice.Customer, isSupplier: false, invoice.Resolution.Prefix));
        root.Add(PaymentMeans(invoice));
        root.Add(PaymentTerms(invoice.Payment));
        AddAllowanceCharges(root, invoice.AllowanceCharges, invoice.LocalCurrencyCode);
        if (invoice.ForeignCurrency is { CalculationRate: { } rate, CalculationRateDate: { } rateDate } foreign)
        {
            root.Add(new UblElement("cac:PaymentExchangeRate")
                .Add("cbc:SourceCurrencyCode", foreign.CurrencyCode)
                .Add("cbc:SourceCurrencyBaseRate", "1.00")
                .Add("cbc:TargetCurrencyCode", invoice.LocalCurrencyCode)
                .Add("cbc:TargetCurrencyBaseRate", "1.00")
                .Add("cbc:CalculationRate", DianRounding.FormatAmount(rate))
                .Add("cbc:Date", Date(rateDate)));
        }

        AddTaxTotals(root, invoice.TaxSubtotals, invoice.LocalCurrencyCode);

        // ApiDian escribe las retenciones en la moneda del documento (la extranjera, si la hay), a diferencia
        // de todos los demás valores, que van en la moneda local («Reglas del PHP», sección 3.2, ítem 6).
        AddWithholdingTotals(root, invoice.WithholdingSubtotals, invoice.ForeignCurrency?.CurrencyCode ?? invoice.LocalCurrencyCode);
        root.Add(LegalMonetaryTotal(invoice.Totals, invoice.LocalCurrencyCode));
        for (var i = 0; i < invoice.Lines.Count; i++)
        {
            root.Add(Line(i + 1, invoice.Lines[i], invoice.LocalCurrencyCode));
        }

        return new InvoiceUblResult(UblXmlSerializer.Serialize(root), cufe);
    }

    private static string ComputeCufe(SalesInvoice invoice)
    {
        var input = new CufeInput(
            invoice.DocumentNumber,
            invoice.IssueDate,
            invoice.IssueTime,
            invoice.Totals.LineExtensionAmount,
            TaxTotalOf(invoice.TaxSubtotals, IvaCode),
            TaxTotalOf(invoice.TaxSubtotals, IncCode),
            TaxTotalOf(invoice.TaxSubtotals, IcaCode),
            invoice.Totals.PayableAmount,
            invoice.Supplier.IdentificationNumber,
            invoice.Customer.IdentificationNumber,
            invoice.Environment);
        return CufeCalculator.ComputeCufe(input, invoice.Resolution.TechnicalKey);
    }

    /// <summary>
    /// Total de un tipo de impuesto (esquema tributario, por ejemplo 01 IVA), como va en su cac:TaxTotal:
    /// la suma sin redondear de sus subtotales, que se redondea solo al escribirla. Devuelve null si el
    /// documento no tiene ese impuesto.
    /// </summary>
    private static decimal? TaxTotalOf(IEnumerable<TaxSubtotal> subtotals, string schemeCode)
    {
        var matching = subtotals.Where(s => s.SchemeCode == schemeCode).ToList();
        return matching.Count == 0 ? null : matching.Sum(s => s.TaxAmount);
    }

    private static UblElement Extensions(SalesInvoice invoice, string cufe)
    {
        var resolution = invoice.Resolution;
        var supplier = invoice.Supplier;
        var authorizedInvoices = new UblElement("sts:AuthorizedInvoices");
        if (!string.IsNullOrEmpty(resolution.Prefix))
        {
            authorizedInvoices.Add("sts:Prefix", resolution.Prefix);
        }

        authorizedInvoices
            .Add("sts:From", resolution.From.ToString(CultureInfo.InvariantCulture))
            .Add("sts:To", resolution.To.ToString(CultureInfo.InvariantCulture));

        var providerId = DianAgency(new UblElement("sts:ProviderID", supplier.IdentificationNumber));
        if (supplier.IdentificationTypeCode is "31" or "13")
        {
            providerId.Attr("schemeID", supplier.CheckDigit ?? "").Attr("schemeName", "31");
        }
        else
        {
            providerId.Attr("schemeName", supplier.IdentificationTypeCode);
        }

        var dianExtensions = new UblElement("sts:DianExtensions")
            .Add(new UblElement("sts:InvoiceControl")
                .Add("sts:InvoiceAuthorization", resolution.AuthorizationNumber)
                .Add(new UblElement("sts:AuthorizationPeriod")
                    .Add("cbc:StartDate", Date(resolution.StartDate))
                    .Add("cbc:EndDate", Date(resolution.EndDate)))
                .Add(authorizedInvoices))
            .Add(new UblElement("sts:InvoiceSource")
                .Add(new UblElement("cbc:IdentificationCode", supplier.Address!.CountryCode)
                    .Attr("listAgencyID", "6")
                    .Attr("listAgencyName", "United Nations Economic Commission for Europe")
                    .Attr("listSchemeURI", "urn:oasis:names:specification:ubl:codelist:gc:CountryIdentificationCode-2.1")))
            .Add(new UblElement("sts:SoftwareProvider")
                .Add(providerId)
                .Add(DianAgency(new UblElement("sts:SoftwareID", invoice.Software.SoftwareId))))
            .Add(DianAgency(new UblElement(
                "sts:SoftwareSecurityCode",
                SoftwareSecurityCode.Compute(invoice.Software.SoftwareId, invoice.Software.Pin, invoice.DocumentNumber))))
            .Add(new UblElement("sts:AuthorizationProvider")
                .Add(DianAgency(new UblElement("sts:AuthorizationProviderID", AuthorizationProviderNit))
                    .Attr("schemeID", "4")
                    .Attr("schemeName", "31")))
            .Add("sts:QRCode", QrUrl(invoice.Environment) + cufe);

        return new UblElement("ext:UBLExtensions")
            .Add(new UblElement("ext:UBLExtension")
                .Add(new UblElement("ext:ExtensionContent").Add(dianExtensions)))
            .Add(new UblElement("ext:UBLExtension")
                .Add(new UblElement("ext:ExtensionContent")));
    }

    private static UblElement Party(string node, InvoiceParty party, bool isSupplier, string prefix)
    {
        var partyElement = new UblElement("cac:Party");
        if (party.OrganizationTypeCode == "2")
        {
            partyElement.Add(new UblElement("cac:PartyIdentification").Add(CompanyId("cbc:ID", party)));
        }

        partyElement.Add(new UblElement("cac:PartyName").Add("cbc:Name", party.Name));
        if (party.Address is { } address)
        {
            // ApiDian quita los espacios sobrantes del nombre de la ciudad en todas partes excepto en la
            // ubicación física (cac:PhysicalLocation) del proveedor.
            partyElement.Add(new UblElement("cac:PhysicalLocation")
                .Add(Address("cac:Address", address, isSupplier ? address.CityName : address.CityName.Trim(),
                    PhysicalAddressLinePrefix + address.AddressLine + PhysicalAddressLineSuffix)));
        }

        var taxScheme = new UblElement("cac:PartyTaxScheme")
            .Add("cbc:RegistrationName", party.Name)
            .Add(CompanyId("cbc:CompanyID", party))
            .Add(new UblElement("cbc:TaxLevelCode", party.LiabilityCode).Attr("listName", party.RegimeCode));
        if (party.Address is { } registration)
        {
            taxScheme.Add(Address("cac:RegistrationAddress", registration, registration.CityName.Trim(), registration.AddressLine));
        }

        taxScheme.Add(new UblElement("cac:TaxScheme")
            .Add("cbc:ID", party.TaxSchemeCode)
            .Add("cbc:Name", party.TaxSchemeName));
        partyElement.Add(taxScheme);

        var corporateRegistration = new UblElement("cac:CorporateRegistrationScheme");
        if (isSupplier)
        {
            corporateRegistration.Add("cbc:ID", prefix);
        }

        corporateRegistration.Add("cbc:Name", party.MerchantRegistration);
        partyElement.Add(new UblElement("cac:PartyLegalEntity")
            .Add("cbc:RegistrationName", party.Name)
            .Add(CompanyId("cbc:CompanyID", party))
            .Add(corporateRegistration));

        if (Contact(party, isSupplier) is { } contact)
        {
            partyElement.Add(contact);
        }

        return new UblElement(node)
            .Add("cbc:AdditionalAccountID", party.OrganizationTypeCode)
            .Add(partyElement);
    }

    private static UblElement? Contact(InvoiceParty party, bool isSupplier)
    {
        if (party.IsFinalConsumer)
        {
            return null;
        }

        var contact = new UblElement("cac:Contact");
        if (isSupplier)
        {
            return contact
                .Add("cbc:Telephone", party.Telephone ?? "")
                .Add("cbc:ElectronicMail", party.Email ?? "");
        }

        if (!string.IsNullOrEmpty(party.Telephone))
        {
            contact.Add("cbc:Telephone", party.Telephone);
        }

        if (!string.IsNullOrEmpty(party.Email))
        {
            contact.Add("cbc:ElectronicMail", party.Email);
        }

        return contact.Children.Count > 0 ? contact : null;
    }

    private static UblElement CompanyId(string name, InvoiceParty party) =>
        DianAgency(new UblElement(name, party.IdentificationNumber))
            .Attr("schemeID", party.CheckDigit ?? "")
            .Attr("schemeName", party.IdentificationTypeCode);

    private static UblElement Address(string node, PartyAddress address, string cityName, string line) =>
        new UblElement(node)
            .Add("cbc:ID", address.MunicipalityCode)
            .Add("cbc:CityName", cityName)
            .Add("cbc:CountrySubentity", address.DepartmentName.Trim())
            .Add("cbc:CountrySubentityCode", address.DepartmentCode)
            .Add(new UblElement("cac:AddressLine").Add("cbc:Line", line))
            .Add(new UblElement("cac:Country")
                .Add("cbc:IdentificationCode", address.CountryCode)
                .Add(new UblElement("cbc:Name", address.CountryName).Attr("languageID", address.LanguageCode)));

    private static UblElement PaymentMeans(SalesInvoice invoice) =>
        new UblElement("cac:PaymentMeans")
            .Add("cbc:ID", invoice.Payment.FormCode)
            .Add("cbc:PaymentMeansCode", invoice.Payment.MethodCode)
            .Add("cbc:PaymentDueDate", Date(invoice.Payment.DueDate ?? invoice.IssueDate))
            .Add("cbc:PaymentID", "0");

    private static UblElement PaymentTerms(PaymentTerms payment) =>
        new UblElement("cac:PaymentTerms")
            .Add("cbc:ReferenceEventCode", payment.FormCode)
            .Add(new UblElement("cac:SettlementPeriod")
                .Add(new UblElement("cbc:DurationMeasure", (payment.DurationDays ?? 0).ToString(CultureInfo.InvariantCulture))
                    .Attr("unitCode", "DAY")));

    private static void AddAllowanceCharges(UblElement parent, IReadOnlyList<AllowanceCharge> items, string currency)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var element = new UblElement("cac:AllowanceCharge")
                .Add("cbc:ID", (i + 1).ToString(CultureInfo.InvariantCulture))
                .Add("cbc:ChargeIndicator", Bool(item.IsCharge));
            if (!item.IsCharge && item.ReasonCode is not null)
            {
                element.Add("cbc:AllowanceChargeReasonCode", item.ReasonCode);
            }

            var multiplier = item.MultiplierFactorNumeric
                ?? (item.BaseAmount is { } baseAmount && baseAmount != 0m ? item.Amount / baseAmount * 100m : 0m);
            element
                .Add("cbc:AllowanceChargeReason", item.Reason)
                .Add("cbc:MultiplierFactorNumeric", DianRounding.FormatAmount(multiplier))
                .Add(Amount("cbc:Amount", item.Amount, currency));
            if (item.BaseAmount is { } written)
            {
                element.Add(Amount("cbc:BaseAmount", written, currency));
            }

            parent.Add(element);
        }
    }

    /// <summary>
    /// Escribe un cac:TaxTotal por cada tipo de impuesto (esquema tributario), ordenados por código (el
    /// mismo orden de los ID de impuestos de ApiDian), cada uno con sus subtotales en el orden de entrada.
    /// </summary>
    private static void AddTaxTotals(UblElement parent, IReadOnlyList<TaxSubtotal> subtotals, string currency)
    {
        foreach (var group in GroupByScheme(subtotals))
        {
            var total = new UblElement("cac:TaxTotal")
                .Add(Amount("cbc:TaxAmount", group.Sum(s => s.TaxAmount), currency))
                .Add(new UblElement("cbc:RoundingAmount", "0.00").Attr("currencyID", currency));
            foreach (var subtotal in group)
            {
                var element = new UblElement("cac:TaxSubtotal")
                    .Add(Amount("cbc:TaxableAmount", subtotal.TaxableAmount, currency))
                    .Add(Amount("cbc:TaxAmount", subtotal.TaxAmount, currency));
                if (subtotal.FixedValue is { } fixedValue)
                {
                    element
                        .Add(new UblElement("cbc:BaseUnitMeasure", DianRounding.FormatQuantity(fixedValue.BaseUnitMeasure))
                            .Attr("unitCode", fixedValue.UnitCode))
                        .Add(Amount("cbc:PerUnitAmount", fixedValue.PerUnitAmount, currency));
                }

                element.Add(TaxCategory(subtotal, subtotal.Percent is { } p ? DianRounding.FormatAmount(p) : null));
                total.Add(element);
            }

            parent.Add(total);
        }
    }

    /// <summary>
    /// Escribe las retenciones (cac:WithholdingTaxTotal): igual que los impuestos, pero cada valor y
    /// porcentaje con 3 decimales.
    /// </summary>
    private static void AddWithholdingTotals(UblElement parent, IReadOnlyList<TaxSubtotal> subtotals, string currency)
    {
        foreach (var group in GroupByScheme(subtotals))
        {
            var total = new UblElement("cac:WithholdingTaxTotal")
                .Add(WithholdingAmount("cbc:TaxAmount", group.Sum(s => s.TaxAmount), currency));
            foreach (var subtotal in group)
            {
                var element = new UblElement("cac:TaxSubtotal");
                if (subtotal.FixedValue is { } fixedValue)
                {
                    element
                        .Add(new UblElement("cbc:BaseUnitMeasure", DianRounding.FormatQuantity(fixedValue.BaseUnitMeasure))
                            .Attr("unitCode", fixedValue.UnitCode))
                        .Add(WithholdingAmount("cbc:PerUnitAmount", fixedValue.PerUnitAmount, currency));
                }
                else
                {
                    element.Add(WithholdingAmount("cbc:TaxableAmount", subtotal.TaxableAmount, currency));
                }

                element
                    .Add(WithholdingAmount("cbc:TaxAmount", subtotal.TaxAmount, currency))
                    .Add(TaxCategory(subtotal, subtotal.Percent is { } p ? DianRounding.FormatWithholding(p) : null));
                total.Add(element);
            }

            parent.Add(total);
        }
    }

    private static IEnumerable<IGrouping<string, TaxSubtotal>> GroupByScheme(IEnumerable<TaxSubtotal> subtotals) =>
        subtotals.GroupBy(s => s.SchemeCode, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal);

    private static UblElement TaxCategory(TaxSubtotal subtotal, string? percent)
    {
        var category = new UblElement("cac:TaxCategory");
        if (percent is not null)
        {
            category.Add("cbc:Percent", percent);
        }

        return category.Add(new UblElement("cac:TaxScheme")
            .Add("cbc:ID", subtotal.SchemeCode)
            .Add("cbc:Name", subtotal.SchemeName));
    }

    private static UblElement LegalMonetaryTotal(MonetaryTotals totals, string currency)
    {
        var element = new UblElement("cac:LegalMonetaryTotal")
            .Add(Amount("cbc:LineExtensionAmount", totals.LineExtensionAmount, currency))
            .Add(Amount("cbc:TaxExclusiveAmount", totals.TaxExclusiveAmount, currency))
            .Add(Amount("cbc:TaxInclusiveAmount", totals.TaxInclusiveAmount, currency));
        if (totals.AllowanceTotalAmount is { } allowance)
        {
            element.Add(Amount("cbc:AllowanceTotalAmount", allowance, currency));
        }

        if (totals.ChargeTotalAmount is { } charge)
        {
            element.Add(Amount("cbc:ChargeTotalAmount", charge, currency));
        }

        if (totals.PrepaidAmount is { } prepaid)
        {
            element.Add(Amount("cbc:PrepaidAmount", prepaid, currency));
        }

        return element.Add(Amount("cbc:PayableAmount", totals.PayableAmount, currency));
    }

    private static UblElement Line(int number, InvoiceLine line, string currency)
    {
        var element = new UblElement("cac:InvoiceLine")
            .Add("cbc:ID", number.ToString(CultureInfo.InvariantCulture))
            .Add(new UblElement("cbc:InvoicedQuantity", DianRounding.FormatQuantity(line.Quantity)).Attr("unitCode", line.UnitCode))
            .Add(Amount("cbc:LineExtensionAmount", line.LineExtensionAmount, currency))
            .Add("cbc:FreeOfChargeIndicator", Bool(line.IsFreeOfCharge));
        if (line.IsFreeOfCharge)
        {
            element.Add(new UblElement("cac:PricingReference")
                .Add(new UblElement("cac:AlternativeConditionPrice")
                    .Add(Amount("cbc:PriceAmount", line.PriceAmount, currency))));
        }

        AddAllowanceCharges(element, line.AllowanceCharges, currency);
        AddTaxTotals(element, line.TaxSubtotals, currency);
        element.Add(new UblElement("cac:Item")
            .Add("cbc:Description", line.Description)
            .Add(new UblElement("cac:StandardItemIdentification")
                .Add(new UblElement("cbc:ID", line.ItemCode)
                    .Attr("schemeID", line.ItemScheme.Code)
                    .Attr("schemeName", line.ItemScheme.Name)
                    .Attr("schemeAgencyID", line.ItemScheme.AgencyId))));
        return element.Add(new UblElement("cac:Price")
            .Add(new UblElement("cbc:PriceAmount", PriceValuePrefix + DianRounding.FormatAmount(line.PriceAmount) + PriceValueSuffix)
                .Attr("currencyID", currency))
            .Add(new UblElement("cbc:BaseQuantity", PriceValuePrefix + DianRounding.FormatQuantity(line.BaseQuantity) + PriceValueSuffix)
                .Attr("unitCode", line.UnitCode)));
    }

    private static UblElement Amount(string name, decimal value, string currency) =>
        new UblElement(name, DianRounding.FormatAmount(value)).Attr("currencyID", currency);

    private static UblElement WithholdingAmount(string name, decimal value, string currency) =>
        new UblElement(name, DianRounding.FormatWithholding(value)).Attr("currencyID", currency);

    private static UblElement DianAgency(UblElement element) =>
        element.Attr("schemeAgencyID", DianAgencyId).Attr("schemeAgencyName", DianAgencyName);

    private static string EnvironmentCode(DianEnvironment environment) => ((int)environment).ToString(CultureInfo.InvariantCulture);

    private static string QrUrl(DianEnvironment environment) => environment == DianEnvironment.Production
        ? "https://catalogo-vpfe.dian.gov.co/document/searchqr?documentkey="
        : "https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=";

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Time(TimeOnly time) => time.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + CufeCalculator.ColombiaOffset;

    private static string Bool(bool value) => value ? "true" : "false";
}
