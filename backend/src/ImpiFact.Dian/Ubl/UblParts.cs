using System.Globalization;
using ImpiFact.Dian.Cufe;
using ImpiFact.Dian.Rounding;

namespace ImpiFact.Dian.Ubl;

/// <summary>
/// Bloques del XML UBL 2.1 (el estándar XML de documentos comerciales que usa la DIAN, la autoridad
/// tributaria de Colombia) que la factura y las notas crédito y débito escriben igual: las extensiones de la
/// DIAN, las partes, el medio de pago, los descuentos y cargos, los impuestos, las retenciones y los totales.
/// </summary>
/// <remarks>
/// Cada bloque reproduce la plantilla compartida de ApiDian (la implementación de referencia en PHP) que lo
/// genera en los tres documentos, así que los casos de referencia (golden) de los tres lo cubren.
/// </remarks>
internal static class UblParts
{
    public const string IvaCode = "01";
    public const string IcaCode = "03";
    public const string IncCode = "04";

    public const string DianAgencyId = "195";
    public const string DianAgencyName = "CO, DIAN (Dirección de Impuestos y Aduanas Nacionales)";
    private const string AuthorizationProviderNit = "800197268";

    // La plantilla de las partes deja saltos de línea y espacios de su propia indentación dentro del texto de
    // la línea de dirección de la ubicación física (cbc:Line). Las fixtures golden los conservan, así que se
    // reproducen literalmente para igualar byte a byte.
    private static readonly string PhysicalAddressLinePrefix = "\n" + new string(' ', 60);
    private static readonly string PhysicalAddressLineSuffix = "\n" + new string(' ', 52);

    /// <summary>
    /// Elemento raíz con los espacios de nombres de ApiDian. <paramref name="schemaLocation"/> se copia tal
    /// cual porque los espacios entre el espacio de nombres y la URL del XSD cambian según el documento.
    /// </summary>
    public static UblElement Root(string name, string schemaLocation) =>
        new UblElement(name)
            .Attr("xmlns", $"urn:oasis:names:specification:ubl:schema:xsd:{name}-2")
            .Attr("xmlns:cac", "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")
            .Attr("xmlns:cbc", "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")
            .Attr("xmlns:ds", "http://www.w3.org/2000/09/xmldsig#")
            .Attr("xmlns:ext", "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2")
            .Attr("xmlns:sts", "urn:dian:gov:co:facturaelectronica:Structures-2-1")
            .Attr("xmlns:xades", "http://uri.etsi.org/01903/v1.3.2#")
            .Attr("xmlns:xades141", "http://uri.etsi.org/01903/v1.4.1#")
            .Attr("xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance")
            .Attr("xsi:schemaLocation", schemaLocation);

    /// <summary>
    /// Proveedor sin dirección: el XML no se puede armar porque el país de la dirección va en
    /// sts:InvoiceSource.
    /// </summary>
    public static void RequireSupplierAddress(InvoiceParty supplier, string paramName)
    {
        if (supplier.Address is null)
        {
            throw new ArgumentException("The supplier must have an address.", paramName);
        }
    }

    /// <summary>
    /// Valores con los que se calcula el CUFE (código único de la factura electrónica) o el CUDE (código
    /// único de documento electrónico, de las notas): los totales y los impuestos IVA, INC e ICA tal como
    /// se escriben en el XML.
    /// </summary>
    public static CufeInput CufeInputOf(
        string documentNumber,
        DateOnly issueDate,
        TimeOnly issueTime,
        MonetaryTotals totals,
        IReadOnlyList<TaxSubtotal> taxSubtotals,
        InvoiceParty supplier,
        InvoiceParty customer,
        DianEnvironment environment) =>
        new(
            documentNumber,
            issueDate,
            issueTime,
            totals.LineExtensionAmount,
            TaxTotalOf(taxSubtotals, IvaCode),
            TaxTotalOf(taxSubtotals, IncCode),
            TaxTotalOf(taxSubtotals, IcaCode),
            totals.PayableAmount,
            supplier.IdentificationNumber,
            customer.IdentificationNumber,
            environment);

    /// <summary>
    /// Escribe ext:UBLExtensions: las extensiones de la DIAN (sts:DianExtensions) y, en el segundo
    /// ext:ExtensionContent, el lugar vacío reservado para la firma. <paramref name="invoiceControl"/> es el
    /// bloque de la resolución de numeración que solo lleva la factura.
    /// </summary>
    public static UblElement Extensions(
        UblElement? invoiceControl,
        InvoiceParty supplier,
        SoftwareCredentials software,
        string documentNumber,
        string qrCode,
        UblElement? sectorExtension = null)
    {
        var providerId = DianAgency(new UblElement("sts:ProviderID", supplier.IdentificationNumber));
        if (supplier.IdentificationTypeCode is "31" or "13")
        {
            providerId.Attr("schemeID", supplier.CheckDigit ?? "").Attr("schemeName", "31");
        }
        else
        {
            providerId.Attr("schemeName", supplier.IdentificationTypeCode);
        }

        var dianExtensions = new UblElement("sts:DianExtensions");
        if (invoiceControl is not null)
        {
            dianExtensions.Add(invoiceControl);
        }

        dianExtensions
            .Add(new UblElement("sts:InvoiceSource")
                .Add(new UblElement("cbc:IdentificationCode", supplier.Address!.CountryCode)
                    .Attr("listAgencyID", "6")
                    .Attr("listAgencyName", "United Nations Economic Commission for Europe")
                    .Attr("listSchemeURI", "urn:oasis:names:specification:ubl:codelist:gc:CountryIdentificationCode-2.1")))
            .Add(new UblElement("sts:SoftwareProvider")
                .Add(providerId)
                .Add(DianAgency(new UblElement("sts:SoftwareID", software.SoftwareId))))
            .Add(DianAgency(new UblElement(
                "sts:SoftwareSecurityCode",
                SoftwareSecurityCode.Compute(software.SoftwareId, software.Pin, documentNumber))))
            .Add(new UblElement("sts:AuthorizationProvider")
                .Add(DianAgency(new UblElement("sts:AuthorizationProviderID", AuthorizationProviderNit))
                    .Attr("schemeID", "4")
                    .Attr("schemeName", "31")))
            .Add("sts:QRCode", qrCode);

        var extensions = new UblElement("ext:UBLExtensions")
            .Add(new UblElement("ext:UBLExtension")
                .Add(new UblElement("ext:ExtensionContent").Add(dianExtensions)));

        // A sector extension (the health one) goes between the DIAN data and the signature (Documento Técnico 2 v001, 6.a).
        if (sectorExtension is not null)
        {
            extensions.Add(sectorExtension);
        }

        return extensions
            .Add(new UblElement("ext:UBLExtension")
                .Add(new UblElement("ext:ExtensionContent")));
    }

    /// <summary>
    /// cac:AccountingSupplierParty o cac:AccountingCustomerParty. El proveedor escribe el prefijo de la
    /// numeración del documento en cac:CorporateRegistrationScheme/cbc:ID, como hace ApiDian.
    /// </summary>
    /// <param name="isSupplier">
    /// True for the party ApiDian's <c>_accounting.blade.php</c> renders as the issuing company (<c>$supplier</c>): the
    /// supplier of an invoice or note, the customer of a support document (the company is the buyer there).
    /// </param>
    /// <param name="supportDocument">
    /// Support document (05) or its adjustment note (95): both parties write the prefix in
    /// cac:CorporateRegistrationScheme/cbc:ID and cac:PartyTaxScheme/cbc:CompanyID always carries schemeName 31, as the
    /// template does for the types 11 and 13 (php-rules 13.2, item 2).
    /// </param>
    public static UblElement Party(string node, InvoiceParty party, bool isSupplier, string prefix, bool supportDocument = false)
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
                    PhysicalAddressLinePrefix + address.AddressLine + PhysicalAddressLineSuffix, party.PostalZone)));
        }

        var taxCompanyId = supportDocument
            ? DianAgency(new UblElement("cbc:CompanyID", party.IdentificationNumber)).Attr("schemeID", party.CheckDigit ?? "").Attr("schemeName", "31")
            : CompanyId("cbc:CompanyID", party);
        var taxScheme = new UblElement("cac:PartyTaxScheme")
            .Add("cbc:RegistrationName", party.Name)
            .Add(taxCompanyId)
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
        if (isSupplier || supportDocument)
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

    /// <summary>
    /// Un cac:PaymentMeans por forma de pago, como <c>_payment_means.blade.php</c> de ApiDian: cbc:PaymentID es el
    /// índice de la forma desde 0 y, sin fecha de vencimiento, cbc:PaymentDueDate es la fecha de emisión.
    /// </summary>
    /// <exception cref="ArgumentException">Si no hay ninguna forma de pago.</exception>
    public static void AddPaymentMeans(UblElement parent, IReadOnlyList<PaymentTerms> payments, DateOnly issueDate, string paramName)
    {
        if (payments.Count == 0)
        {
            throw new ArgumentException("A document needs at least one payment form.", paramName);
        }

        for (var i = 0; i < payments.Count; i++)
        {
            parent.Add(new UblElement("cac:PaymentMeans")
                .Add("cbc:ID", payments[i].FormCode)
                .Add("cbc:PaymentMeansCode", payments[i].MethodCode)
                .Add("cbc:PaymentDueDate", Date(payments[i].DueDate ?? issueDate))
                .Add("cbc:PaymentID", i.ToString(CultureInfo.InvariantCulture)));
        }
    }

    /// <summary>cac:PaymentExchangeRate; null cuando no hay moneda extranjera con tasa y fecha.</summary>
    public static UblElement? PaymentExchangeRate(ForeignCurrency? foreignCurrency, string localCurrency) =>
        foreignCurrency is { CalculationRate: { } rate, CalculationRateDate: { } rateDate } foreign
            ? new UblElement("cac:PaymentExchangeRate")
                .Add("cbc:SourceCurrencyCode", foreign.CurrencyCode)
                .Add("cbc:SourceCurrencyBaseRate", "1.00")
                .Add("cbc:TargetCurrencyCode", localCurrency)
                .Add("cbc:TargetCurrencyBaseRate", "1.00")
                .Add("cbc:CalculationRate", DianRounding.FormatAmount(rate))
                .Add("cbc:Date", Date(rateDate))
            : null;

    public static void AddAllowanceCharges(UblElement parent, IReadOnlyList<AllowanceCharge> items, string currency)
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
    public static void AddTaxTotals(UblElement parent, IReadOnlyList<TaxSubtotal> subtotals, string currency)
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
    public static void AddWithholdingTotals(UblElement parent, IReadOnlyList<TaxSubtotal> subtotals, string currency)
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

    /// <summary>
    /// cac:LegalMonetaryTotal o cac:RequestedMonetaryTotal (nota débito). Los totales opcionales solo se
    /// escriben cuando están presentes.
    /// </summary>
    public static UblElement MonetaryTotal(string node, MonetaryTotals totals, string currency)
    {
        var element = new UblElement(node)
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

    /// <summary>cac:StandardItemIdentification del artículo de una línea.</summary>
    public static UblElement StandardItemIdentification(string itemCode, string schemeId, string schemeName, string schemeAgencyId) =>
        new UblElement("cac:StandardItemIdentification")
            .Add(new UblElement("cbc:ID", itemCode)
                .Attr("schemeID", schemeId)
                .Attr("schemeName", schemeName)
                .Attr("schemeAgencyID", schemeAgencyId));

    public static UblElement Amount(string name, decimal value, string currency) =>
        new UblElement(name, DianRounding.FormatAmount(value)).Attr("currencyID", currency);

    public static string EnvironmentCode(DianEnvironment environment) => ((int)environment).ToString(CultureInfo.InvariantCulture);

    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Time(TimeOnly time) => time.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + CufeCalculator.ColombiaOffset;

    public static string Bool(bool value) => value ? "true" : "false";

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

    private static UblElement Address(string node, PartyAddress address, string cityName, string line, string? postalZone = null)
    {
        var element = new UblElement(node)
            .Add("cbc:ID", address.MunicipalityCode)
            .Add("cbc:CityName", cityName);
        if (postalZone is not null)
        {
            element.Add("cbc:PostalZone", postalZone);
        }

        return element
            .Add("cbc:CountrySubentity", address.DepartmentName.Trim())
            .Add("cbc:CountrySubentityCode", address.DepartmentCode)
            .Add(new UblElement("cac:AddressLine").Add("cbc:Line", line))
            .Add(new UblElement("cac:Country")
                .Add("cbc:IdentificationCode", address.CountryCode)
                .Add(new UblElement("cbc:Name", address.CountryName).Attr("languageID", address.LanguageCode)));
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

    private static UblElement WithholdingAmount(string name, decimal value, string currency) =>
        new UblElement(name, DianRounding.FormatWithholding(value)).Attr("currencyID", currency);

    private static UblElement DianAgency(UblElement element) =>
        element.Attr("schemeAgencyID", DianAgencyId).Attr("schemeAgencyName", DianAgencyName);
}
