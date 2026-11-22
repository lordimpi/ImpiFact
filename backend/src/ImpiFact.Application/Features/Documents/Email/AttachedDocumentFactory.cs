using System.Security.Cryptography.X509Certificates;
using System.Xml;
using ImpiFact.Dian.Attached;
using ImpiFact.Dian.Cufe;
using ImpiFact.Dian.Signing;
using ImpiFact.Dian.Soap;
using ImpiFact.Dian.Ubl;
using ImpiFact.Domain.Catalog;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Features.Documents.Email;

/// <summary>
/// Arma y firma el AttachedDocument (contenedor electrónico, tipo de documento 89 de la DIAN): el XML que se entrega
/// al comprador cuando la DIAN acepta un documento, con el XML firmado del documento y el ApplicationResponse (la
/// respuesta de validación firmada por la DIAN). Es puro: no guarda ni llama a nada.
/// </summary>
/// <remarks>
/// El emisor y el comprador (cac:SenderParty y cac:ReceiverParty) salen de cac:PartyTaxScheme del propio XML firmado
/// que aceptó la DIAN, así el contenedor dice exactamente lo mismo que el documento. El PHP los arma con los datos de
/// la empresa y del cliente de la solicitud, que son los mismos que escribió en ese XML.
/// </remarks>
internal static class AttachedDocumentFactory
{
    private const string Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private const string Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    private static readonly XmlReaderSettings SafeReaderSettings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };

    /// <summary>
    /// El AttachedDocument firmado del documento aceptado. El CUFE o CUDE (códigos únicos de la factura o de la nota)
    /// es el que devolvió la DIAN (XmlDocumentKey) o, si no lo trae, el del documento.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Si el resultado no trae el ApplicationResponse, si este no es un XML con fecha y hora, o si el XML firmado no
    /// tiene el emisor o el comprador.
    /// </exception>
    public static string BuildSigned(Document document, string signedXml, DianResult result, X509Certificate2 certificate, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(result);
        if (result.XmlBase64Bytes is not { Length: > 0 } applicationResponse)
        {
            throw new ArgumentException("The DIAN result has no ApplicationResponse.", nameof(result));
        }

        var key = string.IsNullOrWhiteSpace(result.XmlDocumentKey) ? document.Cufe : result.XmlDocumentKey.Trim();
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException($"Document {document.Id} has no CUFE or CUDE.", nameof(document));
        }

        var (sender, receiver) = ReadParties(signedXml);
        var unsigned = AttachedDocumentBuilder.Build(new AttachedDocumentInput
        {
            Kind = KindOf(document.DocumentTypeId),
            Environment = document.TypeEnvironmentId == TypeEnvironment.Production ? DianEnvironment.Production : DianEnvironment.Testing,
            DocumentKey = key,
            DocumentNumber = document.FullNumber,
            GeneratedAt = now,
            Sender = sender,
            Receiver = receiver,
            SignedDocumentXml = signedXml,
            ApplicationResponseXml = applicationResponse,
        });
        return XmlSigner.Sign(unsigned, certificate, now);
    }

    /// <summary>Emisor (cac:AccountingSupplierParty) y comprador (cac:AccountingCustomerParty) del XML firmado.</summary>
    /// <exception cref="ArgumentException">Si el XML no se puede leer o le falta alguno de los dos.</exception>
    public static (InvoiceParty Sender, InvoiceParty Receiver) ReadParties(string signedXml)
    {
        ArgumentException.ThrowIfNullOrEmpty(signedXml);
        var doc = new XmlDocument { XmlResolver = null };
        try
        {
            // Una marca de orden de bytes (BOM) al inicio del texto no la acepta el lector.
            using var reader = XmlReader.Create(new StringReader(signedXml.TrimStart('﻿')), SafeReaderSettings);
            doc.Load(reader);
        }
        catch (XmlException e)
        {
            throw new ArgumentException("The signed document is not well-formed XML.", nameof(signedXml), e);
        }

        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("cac", Cac);
        ns.AddNamespace("cbc", Cbc);
        return (Party(doc, ns, "cac:AccountingSupplierParty"), Party(doc, ns, "cac:AccountingCustomerParty"));
    }

    private static InvoiceParty Party(XmlDocument doc, XmlNamespaceManager ns, string node)
    {
        var party = doc.DocumentElement!.SelectSingleNode(node, ns) as XmlElement
            ?? throw new ArgumentException($"The signed document has no {node}.", "signedXml");
        var scheme = party.SelectSingleNode("cac:Party/cac:PartyTaxScheme", ns) as XmlElement
            ?? throw new ArgumentException($"The signed document has no {node}/cac:Party/cac:PartyTaxScheme.", "signedXml");
        var companyId = Required(scheme, ns, "cbc:CompanyID", node);
        var taxLevel = Required(scheme, ns, "cbc:TaxLevelCode", node);
        return new InvoiceParty
        {
            OrganizationTypeCode = party.SelectSingleNode("cbc:AdditionalAccountID", ns)?.InnerText ?? string.Empty,
            IdentificationNumber = companyId.InnerText,
            CheckDigit = companyId.GetAttributeNode("schemeID")?.Value is { Length: > 0 } dv ? dv : null,
            IdentificationTypeCode = companyId.GetAttribute("schemeName"),
            Name = Required(scheme, ns, "cbc:RegistrationName", node).InnerText,
            RegimeCode = taxLevel.GetAttribute("listName"),
            LiabilityCode = taxLevel.InnerText,
            TaxSchemeCode = Required(scheme, ns, "cac:TaxScheme/cbc:ID", node).InnerText,
            TaxSchemeName = Required(scheme, ns, "cac:TaxScheme/cbc:Name", node).InnerText,
        };
    }

    private static XmlElement Required(XmlElement scheme, XmlNamespaceManager ns, string path, string node) =>
        scheme.SelectSingleNode(path, ns) as XmlElement
        ?? throw new ArgumentException($"The signed document has no {node}/cac:Party/cac:PartyTaxScheme/{path}.", "signedXml");

    private static AttachedDocumentKind KindOf(int documentTypeId) => documentTypeId switch
    {
        // Factura de venta nacional, de exportación y de contingencia (tipos 03 y 04).
        1 or 2 or DocumentFamilies.ContingencyInvoiceType or DocumentFamilies.DianContingencyInvoiceType => AttachedDocumentKind.Invoice,
        4 => AttachedDocumentKind.CreditNote,
        5 => AttachedDocumentKind.DebitNote,
        DocumentFamilies.SupportDocumentType or DocumentFamilies.SupportDocumentAdjustmentNoteType => AttachedDocumentKind.SupportDocument,
        _ => throw new ArgumentException($"Document type {documentTypeId} has no attached document.", nameof(documentTypeId)),
    };
}
