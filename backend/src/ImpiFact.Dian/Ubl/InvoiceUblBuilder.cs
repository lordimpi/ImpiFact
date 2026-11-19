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
/// electrónica de venta nacional (tipo 01), todavía sin firmar.
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

    // La plantilla de las líneas de ApiDian deja saltos de línea y espacios de su propia indentación dentro
    // del precio (cbc:PriceAmount, cbc:BaseQuantity). Las fixtures golden los conservan, así que se
    // reproducen literalmente para igualar byte a byte. Las notas no tienen este relleno.
    private static readonly string PriceValuePrefix = "\n" + new string(' ', 16);
    private static readonly string PriceValueSuffix = "\n" + new string(' ', 12);

    public static InvoiceUblResult Build(SalesInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        RequireSupplierAddress(invoice.Supplier, nameof(invoice));

        var cufe = CufeCalculator.ComputeCufe(
            CufeInputOf(invoice.DocumentNumber, invoice.IssueDate, invoice.IssueTime, invoice.Totals, invoice.TaxSubtotals,
                invoice.Supplier, invoice.Customer, invoice.Environment),
            invoice.Resolution.TechnicalKey);
        var root = Root("Invoice",
            "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2     http://docs.oasis-open.org/ubl/os-UBL-2.1/xsd/maindoc/UBL-Invoice-2.1.xsd");

        root.Add(Extensions(InvoiceControl(invoice.Resolution), invoice.Supplier, invoice.Software, invoice.DocumentNumber,
            invoice.QrCodeBaseUrl + cufe));
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
        if (invoice.Note is not null)
        {
            root.Add("cbc:Note", invoice.Note);
        }

        root.Add("cbc:DocumentCurrencyCode", invoice.ForeignCurrency?.CurrencyCode ?? invoice.LocalCurrencyCode);
        root.Add("cbc:LineCountNumeric", invoice.Lines.Count.ToString(CultureInfo.InvariantCulture));
        root.Add(Party("cac:AccountingSupplierParty", invoice.Supplier, isSupplier: true, invoice.Resolution.Prefix));
        root.Add(Party("cac:AccountingCustomerParty", invoice.Customer, isSupplier: false, invoice.Resolution.Prefix));
        AddPaymentMeans(root, invoice.Payments, invoice.IssueDate, nameof(invoice));
        root.Add(PaymentTerms(invoice.Payments[0]));

        // Los anticipos van en la moneda del documento, como en ApiDian y la regla FBD04 del anexo.
        foreach (var prepaid in invoice.PrepaidPayments)
        {
            root.Add(PrepaidPayment(prepaid, invoice.ForeignCurrency?.CurrencyCode ?? invoice.LocalCurrencyCode));
        }

        AddAllowanceCharges(root, invoice.AllowanceCharges, invoice.LocalCurrencyCode);
        if (PaymentExchangeRate(invoice.ForeignCurrency, invoice.LocalCurrencyCode) is { } exchangeRate)
        {
            root.Add(exchangeRate);
        }

        AddTaxTotals(root, invoice.TaxSubtotals, invoice.LocalCurrencyCode);

        // ApiDian escribe las retenciones en la moneda del documento (la extranjera, si la hay), a diferencia
        // de todos los demás valores, que van en la moneda local («Reglas del PHP», sección 3.2, ítem 6).
        AddWithholdingTotals(root, invoice.WithholdingSubtotals, invoice.ForeignCurrency?.CurrencyCode ?? invoice.LocalCurrencyCode);
        root.Add(MonetaryTotal("cac:LegalMonetaryTotal", invoice.Totals, invoice.LocalCurrencyCode));
        for (var i = 0; i < invoice.Lines.Count; i++)
        {
            root.Add(Line(i + 1, invoice.Lines[i], invoice.LocalCurrencyCode));
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
        var element = new UblElement("cac:PrepaidPayment")
            .Add("cbc:ID", prepaid.Id)
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
            .Add(StandardItemIdentification(line.ItemCode, line.ItemScheme.Code, line.ItemScheme.Name, line.ItemScheme.AgencyId)));
        return element.Add(new UblElement("cac:Price")
            .Add(new UblElement("cbc:PriceAmount", PriceValuePrefix + DianRounding.FormatAmount(line.PriceAmount) + PriceValueSuffix)
                .Attr("currencyID", currency))
            .Add(new UblElement("cbc:BaseQuantity", PriceValuePrefix + DianRounding.FormatQuantity(line.BaseQuantity) + PriceValueSuffix)
                .Attr("unitCode", line.UnitCode)));
    }
}
