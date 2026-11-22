using System.Globalization;
using ImpiFact.Dian.Cufe;
using ImpiFact.Dian.Rounding;
using static ImpiFact.Dian.Ubl.UblParts;

namespace ImpiFact.Dian.Ubl;

/// <summary>
/// Resultado de construir una factura: el XML antes de firmar y su CUFE (código único de la factura
/// electrónica: un hash que identifica la factura ante la DIAN).
/// </summary>
public sealed record InvoiceUblResult(string Xml, string Cufe);

/// <summary>
/// Construye el XML UBL 2.1 (el estándar XML de documentos comerciales que usa la DIAN) de una factura
/// electrónica de venta nacional (tipo 01), todavía sin firmar. Its variants (AIU, mandate, transport, export, type 02,
/// and the support document, type 05) are options of <see cref="SalesInvoice"/>.
/// </summary>
/// <remarks>
/// La salida ya trae los campos que la implementación de referencia (ApiDian, en PHP) llena en su paso de
/// firma: cbc:UUID (el CUFE), sts:SoftwareSecurityCode y sts:QRCode. El segundo ext:ExtensionContent queda
/// vacío: es el lugar reservado para la firma. El orden de los elementos, cuáles elementos opcionales
/// aparecen y el formato siguen byte a byte los casos de referencia (golden): XML de ApiDian contra los
/// que las pruebas comparan la salida; todo número pasa por <see cref="DianRounding"/>. Los bloques que
/// comparte con las notas crédito y débito están en <see cref="UblParts"/>.
/// </remarks>
public static class InvoiceUblBuilder
{
    private const string CufeAlgorithm = "CUFE-SHA384";

    private const string CudsAlgorithm = "CUDS-SHA384";

    private const string CudeAlgorithm = "CUDE-SHA384";

    /// <summary>
    /// cbc:ProfileID of template 05, with its final period: the library compares this exact text to choose the CUDS
    /// (php-rules 13.2, item 1).
    /// </summary>
    public const string SupportDocumentProfile = "DIAN 2.1: documento soporte en adquisiciones efectuadas a no obligados a facturar.";

    /// <summary>Name of the responsible of the health extension (Documento Técnico 2 v001, section 6.a).</summary>
    public const string HealthResponsibleName = "Responsable";

    /// <summary>The literal the Documento Técnico 2 v001 gives for CustomTagGeneral/Value[1].</summary>
    public const string HealthResponsible = "url www.minsalud.gov.co";

    /// <summary>CustomTagGeneral/Name[2] of the health extension.</summary>
    public const string HealthActName = "Tipo, identificador:año del acto administrativo";

    /// <summary>
    /// CustomTagGeneral/Value[2]: «Resolución NNNN:AAAA» with the act that adopted the Documento Técnico 2 v001, Resolución
    /// 948 de 2026 (php-rules 18, open question 12: the document does not spell the number out).
    /// </summary>
    public const string HealthAct = "Resolución 0948:2026";

    /// <summary>Exact line description that gets the AIU note (php-rules 10.1, item 3: with the accent, case-sensitive).</summary>
    public const string AiuAdministrationLine = "Administración";

    /// <summary>Text before the AIU contract object in the note of the administration line.</summary>
    public const string AiuNotePrefix = "Contrato de servicios AIU por concepto de: ";

    /// <summary>Identification type of the mandator (31 = NIT), the schemeName of its cbc:ID.</summary>
    private const string NitIdentificationType = "31";

    // La plantilla de las líneas de ApiDian deja saltos de línea y espacios de su propia indentación dentro
    // del precio (cbc:PriceAmount, cbc:BaseQuantity). Las fixtures golden los conservan, así que se
    // reproducen literalmente para igualar byte a byte. Las notas no tienen este relleno.
    private static readonly string PriceValuePrefix = "\n" + new string(' ', 16);
    private static readonly string PriceValueSuffix = "\n" + new string(' ', 12);

    public static InvoiceUblResult Build(SalesInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        var supportDocument = invoice.SupportDocument;
        var isSupport = supportDocument is not null;

        // In a support document the issuing company (the software provider) is the customer of the XML.
        var provider = isSupport ? invoice.Customer : invoice.Supplier;
        RequireSupplierAddress(provider, nameof(invoice));

        var codeInput = CufeInputOf(invoice.DocumentNumber, invoice.IssueDate, invoice.IssueTime, invoice.Totals, invoice.TaxSubtotals,
            invoice.Supplier, invoice.Customer, invoice.Environment);

        // php-rules 13.3, item 2: the PHP wrote a CUFE when the type 11 resolution had a technical key; a support document
        // always gets its CUDS. A contingency type 03 transcription gets the CUDE with the PIN (annex 1.9, 11.1.4, Nota-2).
        var isTranscription = invoice.PaperInvoice is not null;
        var cufe = isSupport ? CufeCalculator.ComputeCuds(codeInput, invoice.Software.Pin)
            : isTranscription ? CufeCalculator.ComputeCude(codeInput, invoice.Software.Pin)
            : CufeCalculator.ComputeCufe(codeInput, invoice.Resolution.TechnicalKey);
        var root = Root("Invoice",
            "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2     http://docs.oasis-open.org/ubl/os-UBL-2.1/xsd/maindoc/UBL-Invoice-2.1.xsd");

        var health = invoice.HealthSector;
        var extensions = Extensions(InvoiceControl(invoice.Resolution), provider, invoice.Software, invoice.DocumentNumber,
            invoice.QrCodeBaseUrl + cufe, health?.Fields is { } healthFields ? HealthExtension(healthFields) : null);
        if (invoice.Export?.Supplement is { } supplement)
        {
            extensions.Add(SupplementK(supplement));
        }

        root.Add(extensions);
        root.Add("cbc:UBLVersionID", "UBL 2.1");
        root.Add("cbc:CustomizationID", health?.OperationCode ?? invoice.OperationTypeCode);
        root.Add("cbc:ProfileID", isSupport ? SupportDocumentProfile : "DIAN 2.1: Factura Electrónica de Venta");
        root.Add("cbc:ProfileExecutionID", EnvironmentCode(invoice.Environment));
        root.Add("cbc:ID", invoice.DocumentNumber);
        root.Add(new UblElement("cbc:UUID", cufe)
            .Attr("schemeID", EnvironmentCode(invoice.Environment))
            .Attr("schemeName", isSupport ? CudsAlgorithm : isTranscription ? CudeAlgorithm : CufeAlgorithm));
        root.Add("cbc:IssueDate", Date(invoice.IssueDate));
        root.Add("cbc:IssueTime", Time(invoice.IssueTime));
        root.Add("cbc:InvoiceTypeCode", invoice.InvoiceTypeCode);
        if (invoice.Note is not null)
        {
            root.Add("cbc:Note", invoice.Note);
        }

        var documentCurrency = invoice.ForeignCurrency?.CurrencyCode ?? invoice.LocalCurrencyCode;
        var currency = invoice.AmountsInDocumentCurrency ? documentCurrency : invoice.LocalCurrencyCode;
        root.Add("cbc:DocumentCurrencyCode", documentCurrency);
        root.Add("cbc:LineCountNumeric", invoice.Lines.Count.ToString(CultureInfo.InvariantCulture));

        // Documento Técnico 2 v001, section 7.a: the billing period of a health invoice, in the place of ApiDian's template 01.
        if (health is not null)
        {
            root.Add(new UblElement("cac:InvoicePeriod")
                .Add("cbc:StartDate", Date(health.PeriodStartDate))
                .Add("cbc:EndDate", Date(health.PeriodEndDate)));
        }

        // Template 03 writes the paper invoice right after cbc:LineCountNumeric (php-rules 12.1, item 2; open question 2 of
        // section 18: the UBL 2.1 schema orders cac:OrderReference first, which no ImpiFact invoice writes yet).
        if (invoice.PaperInvoice is { } paper)
        {
            root.Add(new UblElement("cac:AdditionalDocumentReference")
                .Add("cbc:ID", paper.Id)
                .Add("cbc:IssueDate", Date(paper.IssueDate))
                .Add("cbc:DocumentTypeCode", paper.DocumentTypeCode));
        }

        root.Add(Party("cac:AccountingSupplierParty", invoice.Supplier, isSupplier: !isSupport, invoice.Resolution.Prefix, isSupport));
        root.Add(Party("cac:AccountingCustomerParty", invoice.Customer, isSupplier: isSupport, invoice.Resolution.Prefix, isSupport));
        if (invoice.Export is { } export)
        {
            root.Add(new UblElement("cac:DeliveryTerms")
                .Add("cbc:SpecialTerms", export.DeliveryTerms.SpecialTerms)
                .Add("cbc:LossRiskResponsibilityCode", export.DeliveryTerms.LossRiskResponsibilityCode)
                .Add("cbc:LossRisk", export.DeliveryTerms.LossRisk));
        }

        AddPaymentMeans(root, invoice.Payments, invoice.IssueDate, nameof(invoice));

        // Template 02 (export) has its cac:PaymentTerms commented out (php-rules 11, item 3).
        if (invoice.Export is null)
        {
            root.Add(PaymentTerms(invoice.Payments[0]));
        }

        // Los anticipos van en la moneda del documento, como en ApiDian y la regla FBD04 del anexo.
        foreach (var prepaid in invoice.PrepaidPayments)
        {
            root.Add(PrepaidPayment(prepaid, documentCurrency));
        }

        AddAllowanceCharges(root, invoice.AllowanceCharges, currency);
        if (PaymentExchangeRate(invoice.ForeignCurrency, invoice.LocalCurrencyCode) is { } exchangeRate)
        {
            root.Add(exchangeRate);
        }
        else if (supportDocument is not null)
        {
            root.Add(LocalExchangeRate(invoice.LocalCurrencyCode, supportDocument.ExchangeRateDate));
        }

        AddTaxTotals(root, invoice.TaxSubtotals, currency);

        // ApiDian escribe las retenciones en la moneda del documento (la extranjera, si la hay), a diferencia
        // de todos los demás valores, que van en la moneda local («Reglas del PHP», sección 3.2, ítem 6).
        AddWithholdingTotals(root, invoice.WithholdingSubtotals, documentCurrency);
        root.Add(MonetaryTotal("cac:LegalMonetaryTotal", invoice.Totals, currency));
        for (var i = 0; i < invoice.Lines.Count; i++)
        {
            root.Add(Line(i + 1, invoice.Lines[i], currency, invoice));
        }

        return new InvoiceUblResult(UblXmlSerializer.Serialize(root), cufe);
    }

    /// <summary>sts:InvoiceControl: la resolución de numeración que autoriza el número; solo la lleva la factura.</summary>
    private static UblElement InvoiceControl(NumberingResolution resolution)
    {
        var authorizedInvoices = new UblElement("sts:AuthorizedInvoices");
        if (!string.IsNullOrEmpty(resolution.Prefix))
        {
            authorizedInvoices.Add("sts:Prefix", resolution.Prefix);
        }

        authorizedInvoices
            .Add("sts:From", resolution.From.ToString(CultureInfo.InvariantCulture))
            .Add("sts:To", resolution.To.ToString(CultureInfo.InvariantCulture));

        return new UblElement("sts:InvoiceControl")
            .Add("sts:InvoiceAuthorization", resolution.AuthorizationNumber)
            .Add(new UblElement("sts:AuthorizationPeriod")
                .Add("cbc:StartDate", Date(resolution.StartDate))
                .Add("cbc:EndDate", Date(resolution.EndDate)))
            .Add(authorizedInvoices);
    }

    /// <summary>
    /// Third ext:UBLExtension of an export invoice (annex 1.9, supplement K), as ApiDian's <c>_ubl_extensions.blade.php</c>
    /// writes it: unprefixed elements, lowercase <c>name</c>/<c>value</c> and the values as they arrive.
    /// </summary>
    /// <exception cref="ArgumentException">If a list does not have one value per field.</exception>
    private static UblElement SupplementK(ExportSupplement supplement)
    {
        if (supplement.AdditionalInformation.Count != ExportSupplement.AdditionalInformationFields.Count
            || supplement.CopTotals.Count != ExportSupplement.CopTotalFields.Count)
        {
            throw new ArgumentException("The export supplement needs one value per field.", nameof(supplement));
        }

        var collection = new UblElement("Collection").Attr("schemeName", "DATOS ADICIONALES");
        for (var i = 0; i < supplement.AdditionalInformation.Count; i++)
        {
            collection.Add(new UblElement("AdditionalInformation")
                .Add("name", ExportSupplement.AdditionalInformationFields[i].Name)
                .Add("value", supplement.AdditionalInformation[i]));
        }

        var copTotals = new UblElement("TotalesCop");
        for (var i = 0; i < supplement.CopTotals.Count; i++)
        {
            copTotals.Add(ExportSupplement.CopTotalFields[i], supplement.CopTotals[i]);
        }

        return new UblElement("ext:UBLExtension")
            .Add(new UblElement("ext:ExtensionContent")
                .Add(new UblElement("CustomTagGeneral")
                    .Add(new UblElement("Interoperabilidad")
                        .Add(new UblElement("Group").Attr("schemeName", "Exportación").Add(collection)))
                    .Add(copTotals)));
    }

    /// <summary>
    /// The cac:PaymentExchangeRate that ApiDian's support-document controller writes without <c>idcurrency</c>: the company
    /// currency to itself, rate <c>1</c> (an integer in the PHP, so without decimals) and the day of the request.
    /// </summary>
    private static UblElement LocalExchangeRate(string localCurrency, DateOnly date) =>
        new UblElement("cac:PaymentExchangeRate")
            .Add("cbc:SourceCurrencyCode", localCurrency)
            .Add("cbc:SourceCurrencyBaseRate", "1.00")
            .Add("cbc:TargetCurrencyCode", localCurrency)
            .Add("cbc:TargetCurrencyBaseRate", "1.00")
            .Add("cbc:CalculationRate", "1")
            .Add("cbc:Date", Date(date));

    private static UblElement PaymentTerms(PaymentTerms payment) =>
        new UblElement("cac:PaymentTerms")
            .Add("cbc:ReferenceEventCode", payment.FormCode)
            .Add(new UblElement("cac:SettlementPeriod")
                .Add(new UblElement("cbc:DurationMeasure", (payment.DurationDays ?? 0).ToString(CultureInfo.InvariantCulture))
                    .Attr("unitCode", "DAY")));

    /// <summary>
    /// cac:PrepaidPayment con el orden de las plantillas de ApiDian (<c>_prepaid_payment.blade.php</c>) y del anexo
    /// técnico 1.9 (grupo FBD). ApiDian escribe cbc:PaidDate siempre, aunque esté vacío, y en la lista
    /// <c>prepaid_payments</c> pone el mismo ID como schemeID; aquí cbc:PaidDate se omite si no llega y cbc:ID va sin
    /// atributos, como lo pide el anexo fuera del sector salud (php-rules, sección 3.2).
    /// </summary>
    private static UblElement PrepaidPayment(PrepaidPayment prepaid, string currency)
    {
        var id = new UblElement("cbc:ID", prepaid.Id);
        if (prepaid.ConceptCode is not null)
        {
            id.Attr("schemeID", prepaid.ConceptCode);
        }

        var element = new UblElement("cac:PrepaidPayment")
            .Add(id)
            .Add(Amount("cbc:PaidAmount", prepaid.PaidAmount, currency))
            .Add("cbc:ReceivedDate", Date(prepaid.ReceivedDate));
        if (prepaid.PaidDate is { } paidDate)
        {
            element.Add("cbc:PaidDate", Date(paidDate));
        }

        if (prepaid.InstructionId is not null)
        {
            element.Add("cbc:InstructionID", prepaid.InstructionId);
        }

        return element;
    }

    /// <summary>
    /// Second ext:UBLExtension of a health-sector invoice, as the Ministerio de Salud's Documento Técnico 2 v001 (15-jul-2026,
    /// Resolución 948 de 2026), section 6.a, defines it: the responsible and the administrative act as Name/Value pairs of
    /// CustomTagGeneral, then one Collection «Usuario» with CODIGO_PRESTADOR, MODALIDAD_PAGO, COBERTURA_PLAN_BENEFICIOS,
    /// NUMERO_CONTRATO and NUMERO_POLIZA (empty when absent) and, when present, FACTURA_SIN_CONTRATO. Unprefixed elements, like
    /// the supplement K. The DIAN does not validate it (annex 1.9, section 19.1); the Ministry's validator does.
    /// </summary>
    private static UblElement HealthExtension(HealthSectorFields fields)
    {
        var collection = new UblElement("Collection").Attr("schemeName", "Usuario")
            .Add(HealthInformation("CODIGO_PRESTADOR", new UblElement("Value", fields.ProviderCode)))
            .Add(HealthInformation("MODALIDAD_PAGO", HealthValue(fields.PaymentMethod, "salud_modalidad_pago.gc")))
            .Add(HealthInformation("COBERTURA_PLAN_BENEFICIOS", HealthValue(fields.Coverage, "salud_cobertura.gc")))
            .Add(HealthInformation("NUMERO_CONTRATO", new UblElement("Value", fields.ContractNumber)))
            .Add(HealthInformation("NUMERO_POLIZA", new UblElement("Value", fields.PolicyNumber)));
        if (fields.InvoiceWithoutContract is { } withoutContract)
        {
            collection.Add(HealthInformation("FACTURA_SIN_CONTRATO", HealthValue(withoutContract, "salud_facturaSinContrato.gc")));
        }

        return new UblElement("ext:UBLExtension")
            .Add(new UblElement("ext:ExtensionContent")
                .Add(new UblElement("CustomTagGeneral")
                    .Add("Name", HealthResponsibleName)
                    .Add("Value", HealthResponsible)
                    .Add("Name", HealthActName)
                    .Add("Value", HealthAct)
                    .Add(new UblElement("Interoperabilidad")
                        .Add(new UblElement("Group").Attr("schemeName", "Sector Salud").Add(collection)))));
    }

    private static UblElement HealthInformation(string name, UblElement value) =>
        new UblElement("AdditionalInformation").Add("Name", name).Add(value);

    private static UblElement HealthValue(HealthCode code, string table) =>
        new UblElement("Value", code.Name).Attr("schemeName", table).Attr("schemeID", code.Code);

    /// <summary>cac:AdditionalItemProperty of a freight transport line (RNDC data).</summary>
    private static UblElement ItemProperty(string name, string value) =>
        new UblElement("cac:AdditionalItemProperty").Add("cbc:Name", name).Add("cbc:Value", value);

    private static UblElement Line(int number, InvoiceLine line, string currency, SalesInvoice invoice)
    {
        var id = new UblElement("cbc:ID", number.ToString(CultureInfo.InvariantCulture));
        switch (invoice.LineIdScheme)
        {
            case InvoiceLineIdScheme.Mandate:
                id.Attr("schemeID", "1");
                break;
            case InvoiceLineIdScheme.Transport:
                id.Attr("schemeID", line.Consignment is null ? "0" : "1");
                break;
        }

        var element = new UblElement("cac:InvoiceLine").Add(id);
        if (invoice.AiuContractObject is { } aiuObject && line.Description == AiuAdministrationLine)
        {
            element.Add("cbc:Note", AiuNotePrefix + aiuObject);
        }

        if (line.Note is not null)
        {
            element.Add("cbc:Note", line.Note);
        }

        element
            .Add(new UblElement("cbc:InvoicedQuantity", DianRounding.FormatQuantity(line.Quantity)).Attr("unitCode", line.UnitCode))
            .Add(Amount("cbc:LineExtensionAmount", line.LineExtensionAmount, currency));
        if (line.GenerationPeriod is { } period)
        {
            element.Add(new UblElement("cac:InvoicePeriod")
                .Add("cbc:StartDate", Date(period.StartDate))
                .Add("cbc:DescriptionCode", period.DescriptionCode)
                .Add("cbc:Description", period.Description));
        }

        // Template 05 lines (type 11) carry no cbc:FreeOfChargeIndicator (php-rules 13.1, item 5).
        if (invoice.SupportDocument is null)
        {
            element.Add("cbc:FreeOfChargeIndicator", Bool(line.IsFreeOfCharge));
        }
        if (line.IsFreeOfCharge)
        {
            element.Add(new UblElement("cac:PricingReference")
                .Add(new UblElement("cac:AlternativeConditionPrice")
                    .Add(Amount("cbc:PriceAmount", line.PriceAmount, currency))));
        }

        AddAllowanceCharges(element, line.AllowanceCharges, currency);
        AddTaxTotals(element, line.TaxSubtotals, currency);
        var item = new UblElement("cac:Item").Add("cbc:Description", line.Description);
        if (line.BrandName is not null)
        {
            item.Add("cbc:BrandName", line.BrandName);
        }

        if (line.ModelName is not null)
        {
            item.Add("cbc:ModelName", line.ModelName);
        }

        item.Add(StandardItemIdentification(line.ItemCode, line.ItemScheme.Code, line.ItemScheme.Name, line.ItemScheme.AgencyId));
        if (line.Mandator is { } mandator)
        {
            item.Add(new UblElement("cac:InformationContentProviderParty")
                .Add(new UblElement("cac:PowerOfAttorney")
                    .Add(new UblElement("cac:AgentParty")
                        .Add(new UblElement("cac:PartyIdentification")
                            .Add(new UblElement("cbc:ID", mandator.IdentificationNumber)
                                .Attr("schemeAgencyID", DianAgencyId)
                                .Attr("schemeAgencyName", DianAgencyName)
                                .Attr("schemeID", mandator.CheckDigit)
                                .Attr("schemeName", NitIdentificationType))))));
        }

        if (line.Consignment is { } consignment)
        {
            item.Add(ItemProperty("01", consignment.RndcConsignmentNumber))
                .Add(ItemProperty("02", consignment.InternalConsignmentNumber))
                .Add(ItemProperty("03", consignment.Value)
                    .Add(new UblElement("cbc:ValueQuantity", consignment.Quantity).Attr("unitCode", consignment.UnitCode)));
        }

        element.Add(item);
        return element.Add(new UblElement("cac:Price")
            .Add(new UblElement("cbc:PriceAmount", PriceValuePrefix + DianRounding.FormatAmount(line.PriceAmount) + PriceValueSuffix)
                .Attr("currencyID", currency))
            .Add(new UblElement("cbc:BaseQuantity", PriceValuePrefix + DianRounding.FormatQuantity(line.BaseQuantity) + PriceValueSuffix)
                .Attr("unitCode", line.UnitCode)));
    }
}
