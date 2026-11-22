using System.Globalization;
using ImpiFact.Dian.Cufe;
using ImpiFact.Dian.Rounding;
using static ImpiFact.Dian.Ubl.UblParts;

namespace ImpiFact.Dian.Ubl;

/// <summary>
/// Resultado de construir una nota: el XML antes de firmar y su CUDE (código único de documento
/// electrónico: el hash que identifica la nota ante la DIAN, la autoridad tributaria de Colombia).
/// </summary>
public sealed record NoteUblResult(string Xml, string Cude);

/// <summary>
/// Construye el XML UBL 2.1 (el estándar XML de documentos comerciales que usa la DIAN) de una nota crédito
/// (CreditNote, tipo 91) o débito (DebitNote) de factura electrónica de venta, todavía sin firmar.
/// </summary>
/// <remarks>
/// <para>
/// Sigue las plantillas 91.blade.php y 92.blade.php de ApiDian (la implementación de referencia en PHP) y
/// los casos de referencia (golden) capturados de ella. Como en <see cref="InvoiceUblBuilder"/>, la salida ya
/// trae el CUDE en cbc:UUID, sts:SoftwareSecurityCode y sts:QRCode, y deja vacío el segundo
/// ext:ExtensionContent para la firma.
/// </para>
/// <para>
/// Diferencias con la factura: no hay sts:InvoiceControl ni cac:PaymentTerms; el CUDE usa el PIN del
/// software; cada tipo de nota tiene su propio orden de bloques; las líneas escriben su cac:TaxTotal antes
/// de sus descuentos y cargos, y el precio sin el relleno de espacios de la factura.
/// </para>
/// </remarks>
public static class NoteUblBuilder
{
    private const string CudeAlgorithm = "CUDE-SHA384";
    private const string CudsAlgorithm = "CUDS-SHA384";
    private const string CreditNoteTypeCode = "91";
    private const string AdjustmentNoteTypeCode = "95";

    /// <summary>cbc:ProfileID of template 95 (php-rules 14, item 5).</summary>
    public const string AdjustmentNoteProfile =
        "DIAN 2.1: Nota de ajuste al documento soporte en adquisiciones efectuadas a sujetos no obligados a expedir factura o documento equivalente";

    public static NoteUblResult Build(SalesNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        RequireSupplierAddress(note.Kind == NoteKind.SupportDocumentAdjustment ? note.Customer : note.Supplier, nameof(note));
        if (note.Lines.Any(line => line.IsFreeOfCharge && string.IsNullOrEmpty(line.ReferencePriceCode)))
        {
            throw new ArgumentException("A free-of-charge note line needs a reference price code.", nameof(note));
        }

        var isSupport = note.Kind == NoteKind.SupportDocumentAdjustment;
        var isCredit = note.Kind is NoteKind.Credit or NoteKind.SupportDocumentAdjustment;
        var codeInput = CufeInputOf(note.DocumentNumber, note.IssueDate, note.IssueTime, note.Totals, note.TaxSubtotals,
            note.Supplier, note.Customer, note.Environment);

        // php-rules 14, item 6: the adjustment note is identified by its CUDS, with the support-document software PIN.
        var cude = isSupport ? CufeCalculator.ComputeCuds(codeInput, note.Software.Pin) : CufeCalculator.ComputeCude(codeInput, note.Software.Pin);

        // Los espacios entre el espacio de nombres y la URL del XSD son los de cada plantilla: 5 en la nota
        // crédito y 4 en la débito.
        var root = isCredit
            ? Root("CreditNote", "urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2     http://docs.oasis-open.org/ubl/os-UBL-2.1/xsd/maindoc/UBL-CreditNote-2.1.xsd")
            : Root("DebitNote", "urn:oasis:names:specification:ubl:schema:xsd:DebitNote-2    http://docs.oasis-open.org/ubl/os-UBL-2.1/xsd/maindoc/UBL-DebitNote-2.1.xsd");

        // In the adjustment note the issuing company (the software provider) is the customer of the XML.
        root.Add(Extensions(invoiceControl: null, isSupport ? note.Customer : note.Supplier, note.Software, note.DocumentNumber,
            note.QrCodeBaseUrl + cude));
        root.Add("cbc:UBLVersionID", "UBL 2.1");
        root.Add("cbc:CustomizationID", note.OperationTypeCode);
        root.Add("cbc:ProfileID", note.Kind switch
        {
            NoteKind.Credit => "DIAN 2.1: Nota Crédito de Factura Electrónica de Venta",
            NoteKind.Debit => "DIAN 2.1: Nota Débito de Factura Electrónica de Venta",
            _ => AdjustmentNoteProfile,
        });
        root.Add("cbc:ProfileExecutionID", EnvironmentCode(note.Environment));
        root.Add("cbc:ID", note.DocumentNumber);
        root.Add(new UblElement("cbc:UUID", cude)
            .Attr("schemeID", EnvironmentCode(note.Environment))
            .Attr("schemeName", isSupport ? CudsAlgorithm : CudeAlgorithm));
        root.Add("cbc:IssueDate", Date(note.IssueDate));
        root.Add("cbc:IssueTime", Time(note.IssueTime));
        if (isCredit)
        {
            root.Add("cbc:CreditNoteTypeCode", isSupport ? AdjustmentNoteTypeCode : CreditNoteTypeCode);
        }

        if (note.Note is not null)
        {
            root.Add("cbc:Note", note.Note);
        }

        var documentCurrency = note.ForeignCurrency?.CurrencyCode ?? note.LocalCurrencyCode;
        root.Add("cbc:DocumentCurrencyCode", documentCurrency);
        root.Add("cbc:LineCountNumeric", note.Lines.Count.ToString(CultureInfo.InvariantCulture));
        if (note.InvoicePeriod is { } period)
        {
            root.Add(new UblElement("cac:InvoicePeriod")
                .Add("cbc:StartDate", Date(period.StartDate))
                .Add("cbc:EndDate", Date(period.EndDate)));
        }

        // La nota crédito escribe la discrepancia antes de cac:OrderReference y cac:AdditionalDocumentReference,
        // y la débito después; ninguno de esos dos bloques está en el alcance, así que en ambas queda justo
        // antes de cac:BillingReference.
        if (note.Discrepancy is { } discrepancy)
        {
            root.Add(DiscrepancyResponse(discrepancy, note.BillingReference));
        }

        if (note.BillingReference is { } reference)
        {
            root.Add(new UblElement("cac:BillingReference")
                .Add(new UblElement("cac:InvoiceDocumentReference")
                    .Add("cbc:ID", reference.Number)
                    .Add(new UblElement("cbc:UUID", reference.Uuid).Attr("schemeName", reference.SchemeName))
                    .Add("cbc:IssueDate", Date(reference.IssueDate))));
        }

        root.Add(Party("cac:AccountingSupplierParty", note.Supplier, isSupplier: !isSupport, note.Prefix, isSupport));
        root.Add(Party("cac:AccountingCustomerParty", note.Customer, isSupplier: isSupport, note.Prefix, isSupport));
        if (isCredit)
        {
            AddPaymentAndExchangeRate(root, note);
            AddAllowanceCharges(root, note.AllowanceCharges, note.LocalCurrencyCode);
        }
        else
        {
            AddAllowanceCharges(root, note.AllowanceCharges, note.LocalCurrencyCode);
            AddPaymentAndExchangeRate(root, note);
        }

        AddTaxTotals(root, note.TaxSubtotals, note.LocalCurrencyCode);

        // Como en la factura, las retenciones van en la moneda del documento.
        AddWithholdingTotals(root, note.WithholdingSubtotals, documentCurrency);
        root.Add(isCredit
            ? MonetaryTotal("cac:LegalMonetaryTotal", note.Totals, note.LocalCurrencyCode)
            : MonetaryTotal("cac:RequestedMonetaryTotal", note.Totals, note.LocalCurrencyCode));
        for (var i = 0; i < note.Lines.Count; i++)
        {
            root.Add(Line(note.Kind, i + 1, note.Lines[i], note.LocalCurrencyCode, documentCurrency));
        }

        return new NoteUblResult(UblXmlSerializer.Serialize(root), cude);
    }

    private static void AddPaymentAndExchangeRate(UblElement root, SalesNote note)
    {
        AddPaymentMeans(root, note.Payments, note.IssueDate, nameof(note));
        if (PaymentExchangeRate(note.ForeignCurrency, note.LocalCurrencyCode) is { } exchangeRate)
        {
            root.Add(exchangeRate);
        }
    }

    /// <summary>cac:DiscrepancyResponse; cbc:ReferenceID solo cuando la nota se refiere a una factura.</summary>
    private static UblElement DiscrepancyResponse(DiscrepancyResponse discrepancy, BillingReference? reference)
    {
        var element = new UblElement("cac:DiscrepancyResponse");
        if (reference is not null)
        {
            element.Add("cbc:ReferenceID", reference.Number);
        }

        return element
            .Add("cbc:ResponseCode", discrepancy.Code)
            .Add("cbc:Description", discrepancy.Description);
    }

    /// <summary>
    /// cac:CreditNoteLine o cac:DebitNoteLine. El valor de la línea y el precio van en la moneda del
    /// documento; el precio de referencia, los impuestos y los descuentos, en la local (plantillas
    /// _credit_note_lines y _debit_note_lines de ApiDian).
    /// </summary>
    private static UblElement Line(NoteKind kind, int number, InvoiceLine line, string localCurrency, string documentCurrency)
    {
        var isCredit = kind is NoteKind.Credit or NoteKind.SupportDocumentAdjustment;
        var element = new UblElement(isCredit ? "cac:CreditNoteLine" : "cac:DebitNoteLine")
            .Add("cbc:ID", number.ToString(CultureInfo.InvariantCulture))
            .Add(new UblElement(isCredit ? "cbc:CreditedQuantity" : "cbc:DebitedQuantity", DianRounding.FormatQuantity(line.Quantity))
                .Attr("unitCode", line.UnitCode))
            .Add(Amount("cbc:LineExtensionAmount", line.LineExtensionAmount, documentCurrency));
        if (isCredit)
        {
            // La nota débito no lleva el indicador de línea gratuita.
            element.Add("cbc:FreeOfChargeIndicator", Bool(line.IsFreeOfCharge));
        }

        if (line.IsFreeOfCharge)
        {
            element.Add(new UblElement("cac:PricingReference")
                .Add(new UblElement("cac:AlternativeConditionPrice")
                    .Add(Amount("cbc:PriceAmount", line.PriceAmount, localCurrency))
                    .Add("cbc:PriceTypeCode", line.ReferencePriceCode!)));
        }

        AddTaxTotals(element, line.TaxSubtotals, localCurrency);
        AddAllowanceCharges(element, line.AllowanceCharges, localCurrency);

        // La nota crédito escribe el nombre del esquema de identificación del artículo en schemeName; la
        // débito escribe su código. Only the adjustment note writes the brand and the model (template 95 lines).
        var item = new UblElement("cac:Item").Add("cbc:Description", line.Description);
        if (kind == NoteKind.SupportDocumentAdjustment && line.BrandName is not null)
        {
            item.Add("cbc:BrandName", line.BrandName);
        }

        if (kind == NoteKind.SupportDocumentAdjustment && line.ModelName is not null)
        {
            item.Add("cbc:ModelName", line.ModelName);
        }

        element.Add(item.Add(StandardItemIdentification(line.ItemCode, line.ItemScheme.Code,
            isCredit ? line.ItemScheme.Name : line.ItemScheme.Code, line.ItemScheme.AgencyId)));

        // Una línea gratuita escribe 0 como precio; su valor real va en el precio de referencia.
        return element.Add(new UblElement("cac:Price")
            .Add(Amount("cbc:PriceAmount", line.IsFreeOfCharge ? 0m : line.PriceAmount, documentCurrency))
            .Add(new UblElement("cbc:BaseQuantity", DianRounding.FormatQuantity(line.BaseQuantity)).Attr("unitCode", line.UnitCode)));
    }
}
