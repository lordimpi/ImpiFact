using System.Globalization;
using System.Xml;

namespace ImpiFact.Dian.Radian;

/// <summary>
/// The data of an invoice received from a seller, read from its XML (php-rules 16.7, item 2). Amounts are those of
/// cac:LegalMonetaryTotal; the tax amount is <see cref="TaxInclusiveAmount"/> minus <see cref="LineExtensionAmount"/>, as
/// the PHP stores it.
/// </summary>
/// <param name="SupplierDv">Recalculated from the NIT, as the PHP does.</param>
/// <param name="SupplierOrganizationTypeCode">cbc:AdditionalAccountID of the seller (1 legal, 2 natural person).</param>
/// <param name="SupplierRegimeCode">listName of the seller's cbc:TaxLevelCode (48, 49); null when it has none.</param>
/// <param name="Prefix">sts:Prefix of the numbering; empty without one.</param>
/// <param name="Number">cbc:ID of the invoice, with its prefix (for example <c>FPRU1001</c>).</param>
/// <param name="InvoiceTypeCode">cbc:InvoiceTypeCode: 01, 02, 03 or 04.</param>
/// <param name="UuidSchemeName">schemeName of the invoice cbc:UUID (CUFE-SHA384 or, for a type 03, CUDE-SHA384).</param>
/// <param name="Environment">cbc:ProfileExecutionID: 1 production, 2 habilitación.</param>
public sealed record ReceivedInvoice(
    string SupplierNit,
    string SupplierDv,
    string SupplierName,
    string SupplierOrganizationTypeCode,
    string? SupplierRegimeCode,
    string? SupplierEmail,
    string CustomerNit,
    string InvoiceTypeCode,
    string Prefix,
    string Number,
    string Cufe,
    string UuidSchemeName,
    DateOnly IssueDate,
    TimeOnly? IssueTime,
    int Environment,
    string Currency,
    decimal LineExtensionAmount,
    decimal TaxExclusiveAmount,
    decimal TaxInclusiveAmount,
    decimal AllowanceTotalAmount,
    decimal PayableAmount);

/// <summary>
/// Reads a received invoice from the XML the DIAN returns (GetXmlByDocumentKey) or from the AttachedDocument a seller
/// sends (its invoice travels in the CDATA of cac:Attachment/cac:ExternalReference/cbc:Description). The XML is read
/// without DTD nor external resources.
/// </summary>
public static class ReceivedInvoiceReader
{
    private const string InvoiceNs = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private const string AttachedNs = "urn:oasis:names:specification:ubl:schema:xsd:AttachedDocument-2";
    private const string CacNs = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private const string CbcNs = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private const string StsNs = "urn:dian:gov:co:facturaelectronica:Structures-2-1";

    /// <returns>The invoice; null when the XML is not well formed, is not an invoice (or an AttachedDocument with one) or
    /// lacks a required field.</returns>
    public static ReceivedInvoice? Read(byte[] xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        return Read(() => SafeXml.Load(xml));
    }

    /// <inheritdoc cref="Read(byte[])"/>
    public static ReceivedInvoice? Read(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        return Read(() => SafeXml.Load(xml));
    }

    /// <summary>The invoice XML inside an AttachedDocument, or the document itself when it is an invoice; null otherwise.</summary>
    public static string? InvoiceXmlOf(string xml)
    {
        try
        {
            var root = SafeXml.Load(xml).DocumentElement;
            return root switch
            {
                { LocalName: "Invoice", NamespaceURI: InvoiceNs } => xml,
                { LocalName: "AttachedDocument", NamespaceURI: AttachedNs } => EmbeddedInvoice(root),
                _ => null,
            };
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static ReceivedInvoice? Read(Func<XmlDocument> load)
    {
        XmlElement? root;
        try
        {
            root = load().DocumentElement;
            if (root is { LocalName: "AttachedDocument", NamespaceURI: AttachedNs } && EmbeddedInvoice(root) is { } embedded)
            {
                root = SafeXml.Load(embedded).DocumentElement;
            }
        }
        catch (XmlException)
        {
            return null;
        }

        if (root is not { LocalName: "Invoice", NamespaceURI: InvoiceNs })
        {
            return null;
        }

        try
        {
            return Map(root);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static ReceivedInvoice? Map(XmlElement invoice)
    {
        var supplier = Child(invoice, CacNs, "AccountingSupplierParty");
        var supplierParty = Child(supplier, CacNs, "Party");
        var supplierScheme = Child(supplierParty, CacNs, "PartyTaxScheme");
        var customerScheme = Child(Child(Child(invoice, CacNs, "AccountingCustomerParty"), CacNs, "Party"), CacNs, "PartyTaxScheme");
        var totals = Child(invoice, CacNs, "LegalMonetaryTotal");
        var uuid = Child(invoice, CbcNs, "UUID");

        var supplierNit = Text(supplierScheme, "CompanyID");
        var supplierName = Text(supplierScheme, "RegistrationName");
        var customerNit = Text(customerScheme, "CompanyID");
        var number = Text(invoice, "ID");
        var cufe = uuid?.InnerText.Trim();
        var typeCode = Text(invoice, "InvoiceTypeCode");
        var issueDate = Text(invoice, "IssueDate");
        if (supplierNit is null || !supplierNit.All(char.IsAsciiDigit) || supplierNit.Length > 15 || supplierName is null
            || customerNit is null || number is null || string.IsNullOrEmpty(cufe) || typeCode is null || issueDate is null
            || totals is null)
        {
            return null;
        }

        var payable = Child(totals, CbcNs, "PayableAmount");
        return new ReceivedInvoice(
            supplierNit,
            NitCheckDigit.Compute(supplierNit).ToString(CultureInfo.InvariantCulture),
            supplierName,
            Text(supplier, "AdditionalAccountID") ?? "1",
            Child(supplierScheme, CbcNs, "TaxLevelCode")?.GetAttribute("listName") is { Length: > 0 } regime ? regime : null,
            Text(Child(supplierParty, CacNs, "Contact"), "ElectronicMail"),
            customerNit,
            typeCode,
            Prefix(invoice) ?? "",
            number,
            cufe,
            uuid!.GetAttribute("schemeName") is { Length: > 0 } scheme ? scheme : "CUFE-SHA384",
            DateOnly.ParseExact(issueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            Text(invoice, "IssueTime") is { Length: >= 8 } time
                ? TimeOnly.ParseExact(time[..8], "HH:mm:ss", CultureInfo.InvariantCulture)
                : null,
            int.TryParse(Text(invoice, "ProfileExecutionID"), NumberStyles.None, CultureInfo.InvariantCulture, out var environment) ? environment : 2,
            payable?.GetAttribute("currencyID") is { Length: > 0 } currency ? currency : "COP",
            Amount(totals, "LineExtensionAmount"),
            Amount(totals, "TaxExclusiveAmount"),
            Amount(totals, "TaxInclusiveAmount"),
            Amount(totals, "AllowanceTotalAmount"),
            Amount(totals, "PayableAmount"));
    }

    private static string? Prefix(XmlElement invoice) =>
        invoice.GetElementsByTagName("Prefix", StsNs).OfType<XmlElement>().FirstOrDefault()?.InnerText.Trim();

    private static string? EmbeddedInvoice(XmlElement attached)
    {
        // The first cac:Attachment of the AttachedDocument carries the invoice; the one of cac:ParentDocumentLineReference,
        // the DIAN ApplicationResponse.
        var description = Child(Child(Child(attached, CacNs, "Attachment"), CacNs, "ExternalReference"), CbcNs, "Description");
        var text = description?.InnerText.Trim();
        return text is not null && text.Contains("<Invoice", StringComparison.Ordinal) ? text : null;
    }

    private static decimal Amount(XmlElement totals, string localName) =>
        Text(totals, localName) is { } text ? decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture) : 0m;

    private static XmlElement? Child(XmlElement? parent, string ns, string localName) =>
        parent?.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == localName && e.NamespaceURI == ns);

    private static string? Text(XmlElement? parent, string localName) =>
        Child(parent, CbcNs, localName)?.InnerText.Trim() is { Length: > 0 } text ? text : null;
}
