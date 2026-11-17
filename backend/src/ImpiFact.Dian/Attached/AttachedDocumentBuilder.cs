using System.Text;
using System.Xml;
using ImpiFact.Dian.Signing;
using ImpiFact.Dian.Ubl;
using static ImpiFact.Dian.Ubl.UblParts;

namespace ImpiFact.Dian.Attached;

/// <summary>
/// Construye el AttachedDocument (contenedor electrónico, tipo de documento 89 de la DIAN), todavía sin
/// firmar: el XML UBL 2.1 (el estándar XML de documentos comerciales que usa la DIAN) que el emisor
/// entrega al comprador cuando la DIAN acepta una factura o una nota.
/// </summary>
/// <remarks>
/// <para>
/// Lleva dos documentos completos dentro de secciones CDATA (texto que el lector XML no interpreta): el
/// documento firmado, en cac:Attachment, y el ApplicationResponse (la respuesta de validación de la DIAN,
/// firmada por ella), en cac:ParentDocumentLineReference/cac:DocumentReference/cac:Attachment. Ambos se
/// copian sin cambiar un carácter.
/// </para>
/// <para>
/// El orden de los elementos y el formato siguen byte a byte la plantilla 89.blade.php de ApiDian (la
/// implementación de referencia en PHP) y los casos de referencia (golden) capturados de ella. Igual que el
/// PHP, cbc:ValidationDate y cbc:ValidationTime son el primer IssueDate y el primer IssueTime del
/// ApplicationResponse, y la fecha de cac:DocumentReference/cbc:IssueDate es la de generación del
/// contenedor. Única diferencia con el PHP: para las notas, cbc:UUID usa schemeName="CUDE-SHA384", como pide
/// el Anexo Técnico 1.9 (AE43); el PHP escribe CUFE-SHA384 siempre.
/// </para>
/// <para>
/// El ext:ExtensionContent del único ext:UBLExtension queda vacío: es el lugar reservado para la firma, que
/// pone <see cref="XmlSigner.Sign"/>.
/// </para>
/// </remarks>
public static class AttachedDocumentBuilder
{
    private const string ValidatorId = "Unidad Especial Dirección de Impuestos Y Aduanas Nacionales";

    // Código del resultado de la validación (cbc:ValidationResultCode): el PHP escribe 02 (validado) siempre.
    private const string ValidatedResultCode = "02";

    // Marcas temporales del texto de cada cbc:Description. Después de serializar, cada
    // <cbc:Description>marca</cbc:Description> se reemplaza por la sección CDATA. El texto de los datos se
    // escribe escapado (con &lt;), así que esa etiqueta literal solo puede venir de estos dos elementos.
    private const string SignedDocumentMarker = "signed-document";
    private const string ApplicationResponseMarker = "application-response";

    private static readonly TimeSpan ColombiaUtcOffset = TimeSpan.FromHours(-5);

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string Build(AttachedDocumentInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrEmpty(input.DocumentKey, nameof(input));
        ArgumentException.ThrowIfNullOrEmpty(input.DocumentNumber, nameof(input));
        ArgumentException.ThrowIfNullOrEmpty(input.SignedDocumentXml, nameof(input));
        ArgumentNullException.ThrowIfNull(input.Sender, nameof(input));
        ArgumentNullException.ThrowIfNull(input.Receiver, nameof(input));
        ArgumentNullException.ThrowIfNull(input.ApplicationResponseXml, nameof(input));

        var applicationResponse = DecodeApplicationResponse(input.ApplicationResponseXml, nameof(input));
        var (validationDate, validationTime) = ReadValidationMoment(applicationResponse, nameof(input));
        var generatedAt = input.GeneratedAt.ToOffset(ColombiaUtcOffset).DateTime;
        var issueDate = Date(DateOnly.FromDateTime(generatedAt));

        var root = new UblElement("AttachedDocument")
            .Attr("xmlns", "urn:oasis:names:specification:ubl:schema:xsd:AttachedDocument-2")
            .Attr("xmlns:cac", "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")
            .Attr("xmlns:cbc", "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")
            .Attr("xmlns:ccts", "urn:un:unece:uncefact:data:specification:CoreComponentTypeSchemaModule:2")
            .Attr("xmlns:ds", "http://www.w3.org/2000/09/xmldsig#")
            .Attr("xmlns:ext", "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2")
            .Attr("xmlns:xades", "http://uri.etsi.org/01903/v1.3.2#")
            .Attr("xmlns:xades141", "http://uri.etsi.org/01903/v1.4.1#");

        root.Add(new UblElement("ext:UBLExtensions")
            .Add(new UblElement("ext:UBLExtension")
                .Add(new UblElement("ext:ExtensionContent"))));
        root.Add("cbc:UBLVersionID", "UBL 2.1");
        root.Add("cbc:CustomizationID", "Documentos adjuntos");
        root.Add("cbc:ProfileID", "Factura Electrónica de Venta");
        root.Add("cbc:ProfileExecutionID", EnvironmentCode(input.Environment));
        root.Add("cbc:ID", input.DocumentKey);
        root.Add("cbc:IssueDate", issueDate);
        root.Add("cbc:IssueTime", Time(TimeOnly.FromDateTime(generatedAt)));
        root.Add("cbc:DocumentType", "Contenedor de Factura Electrónica");
        root.Add("cbc:ParentDocumentID", input.DocumentNumber);
        root.Add(Party("cac:SenderParty", input.Sender, withOrganizationType: false));
        root.Add(Party("cac:ReceiverParty", input.Receiver, withOrganizationType: true));
        root.Add(Attachment(SignedDocumentMarker));
        root.Add(new UblElement("cac:ParentDocumentLineReference")
            .Add("cbc:LineID", "1")
            .Add(new UblElement("cac:DocumentReference")
                .Add("cbc:ID", input.DocumentNumber)
                .Add(new UblElement("cbc:UUID", input.DocumentKey).Attr("schemeName", KeyScheme(input.Kind)))
                .Add("cbc:IssueDate", issueDate)
                .Add("cbc:DocumentType", "ApplicationResponse")
                .Add(Attachment(ApplicationResponseMarker))
                .Add(new UblElement("cac:ResultOfVerification")
                    .Add("cbc:ValidatorID", ValidatorId)
                    .Add("cbc:ValidationResultCode", ValidatedResultCode)
                    .Add("cbc:ValidationDate", validationDate)
                    .Add("cbc:ValidationTime", validationTime))));

        return EmbedDocuments(UblXmlSerializer.Serialize(root), input.SignedDocumentXml, applicationResponse);
    }

    /// <summary>
    /// cac:SenderParty o cac:ReceiverParty, como _customer_attached.blade.php: solo el receptor lleva el
    /// tipo de organización (persona jurídica o natural) en schemeVersionID. Sin dígito de verificación,
    /// schemeID va vacío, como en el PHP.
    /// </summary>
    private static UblElement Party(string node, InvoiceParty party, bool withOrganizationType)
    {
        var companyId = new UblElement("cbc:CompanyID", party.IdentificationNumber)
            .Attr("schemeAgencyID", DianAgencyId)
            .Attr("schemeAgencyName", DianAgencyName)
            .Attr("schemeID", party.CheckDigit ?? string.Empty)
            .Attr("schemeName", party.IdentificationTypeCode);
        if (withOrganizationType)
        {
            companyId.Attr("schemeVersionID", party.OrganizationTypeCode);
        }

        return new UblElement(node)
            .Add(new UblElement("cac:PartyTaxScheme")
                .Add("cbc:RegistrationName", party.Name)
                .Add(companyId)
                .Add(new UblElement("cbc:TaxLevelCode", party.LiabilityCode).Attr("listName", party.RegimeCode))
                .Add(new UblElement("cac:TaxScheme")
                    .Add("cbc:ID", party.TaxSchemeCode)
                    .Add("cbc:Name", party.TaxSchemeName)));
    }

    private static UblElement Attachment(string descriptionMarker) =>
        new UblElement("cac:Attachment")
            .Add(new UblElement("cac:ExternalReference")
                .Add("cbc:MimeCode", "text/xml")
                .Add("cbc:EncodingCode", "UTF-8")
                .Add("cbc:Description", descriptionMarker));

    private static string KeyScheme(AttachedDocumentKind kind) => kind switch
    {
        AttachedDocumentKind.Invoice => "CUFE-SHA384",
        AttachedDocumentKind.CreditNote or AttachedDocumentKind.DebitNote => "CUDE-SHA384",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown attached document kind."),
    };

    /// <summary>
    /// Pone cada documento como sección CDATA en el lugar de su marca. Las dos posiciones se buscan antes de
    /// insertar nada, así que el contenido embebido nunca se confunde con una marca.
    /// </summary>
    private static string EmbedDocuments(string xml, string signedDocument, string applicationResponse)
    {
        var signedTag = Description(SignedDocumentMarker);
        var responseTag = Description(ApplicationResponseMarker);
        var signedAt = IndexOfSingle(xml, signedTag);
        var responseAt = IndexOfSingle(xml, responseTag);

        return new StringBuilder(xml.Length + signedDocument.Length + applicationResponse.Length + 64)
            .Append(xml, 0, signedAt)
            .Append(Description(Cdata(signedDocument)))
            .Append(xml, signedAt + signedTag.Length, responseAt - signedAt - signedTag.Length)
            .Append(Description(Cdata(applicationResponse)))
            .Append(xml, responseAt + responseTag.Length, xml.Length - responseAt - responseTag.Length)
            .ToString();
    }

    private static string Description(string content) => "<cbc:Description>" + content + "</cbc:Description>";

    private static int IndexOfSingle(string xml, string value)
    {
        var index = xml.IndexOf(value, StringComparison.Ordinal);
        if (index < 0 || xml.IndexOf(value, index + 1, StringComparison.Ordinal) >= 0)
        {
            throw new InvalidOperationException($"Expected exactly one {value} in the serialized document.");
        }

        return index;
    }

    /// <summary>
    /// Sección CDATA con <paramref name="text"/>. Una sección no puede contener "]]>" (su marca de cierre):
    /// como libxml (la biblioteca XML que usa el PHP), cada "]]>" se parte en "]]" al final de una sección y
    /// "&gt;" al inicio de la siguiente. Un lector XML une las secciones y devuelve el texto original.
    /// </summary>
    private static string Cdata(string text) =>
        "<![CDATA[" + text.Replace("]]>", "]]]]><![CDATA[>", StringComparison.Ordinal) + "]]>";

    private static string DecodeApplicationResponse(byte[] bytes, string paramName)
    {
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException e)
        {
            throw new ArgumentException("The ApplicationResponse is not valid UTF-8.", paramName, e);
        }
    }

    /// <summary>
    /// Texto del primer IssueDate y del primer IssueTime del ApplicationResponse, sin cambios, como el PHP
    /// (getElementsByTagName busca por nombre local en cualquier espacio de nombres, bajo la raíz).
    /// </summary>
    private static (string Date, string Time) ReadValidationMoment(string applicationResponse, string paramName)
    {
        var doc = new XmlDocument { XmlResolver = null };
        try
        {
            // Una marca de orden de bytes (BOM) se conserva en el CDATA, pero el lector no la acepta en un texto.
            using var reader = XmlReader.Create(new StringReader(applicationResponse.TrimStart('﻿')), XmlSigner.SafeReaderSettings);
            doc.Load(reader);
        }
        catch (XmlException e)
        {
            throw new ArgumentException("The ApplicationResponse is not well-formed XML.", paramName, e);
        }

        return (FirstText(doc, "IssueDate", paramName), FirstText(doc, "IssueTime", paramName));
    }

    private static string FirstText(XmlDocument doc, string localName, string paramName) =>
        doc.DocumentElement!.SelectSingleNode($"descendant::*[local-name()='{localName}'][1]")?.InnerText
        ?? throw new ArgumentException($"The ApplicationResponse has no {localName}.", paramName);
}
