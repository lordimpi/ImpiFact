using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using ImpiFact.Dian.Signing;

namespace ImpiFact.Dian.Soap;

/// <summary>
/// Arma el sobre SOAP 1.2 (protocolo de mensajes XML de servicios web) firmado que se envía al servicio web
/// de la DIAN (la autoridad tributaria de Colombia).
/// </summary>
/// <remarks>
/// <para>
/// El sobre tiene un encabezado (soap:Header) y un cuerpo (soap:Body). El encabezado lleva:
/// <list type="bullet">
/// <item>wsse:Security, de WS-Security (estándar de seguridad para SOAP): una marca de tiempo
/// (wsu:Timestamp) con su vencimiento, el certificado del emisor (wsse:BinarySecurityToken) y una firma
/// digital XML (ds:Signature, estándar XMLDSig).</item>
/// <item>wsa:Action y wsa:To, de WS-Addressing (estándar que pone en el encabezado la operación que se invoca
/// y la dirección del servicio).</item>
/// </list>
/// El cuerpo lleva la operación y sus parámetros (ver <see cref="DianSoapRequest"/>).
/// </para>
/// <para>
/// Qué se firma: solo el elemento wsa:To (la dirección del servicio), igual que ApiDian (la implementación de
/// referencia en PHP). Ni la marca de tiempo ni el cuerpo se firman. La huella (digest: hash SHA-256 del
/// elemento) y la firma RSA-SHA256 se calculan sobre la forma canónica exclusiva del XML (exc-c14n:
/// canonicalización exclusiva, una forma estándar de escribir un fragmento XML para que el mismo contenido
/// produzca siempre los mismos bytes, que solo incluye los espacios de nombres que el fragmento usa más los de
/// una lista de prefijos indicada, la "InclusiveNamespaces PrefixList").
/// </para>
/// <para>
/// La firma RSA que se usa (relleno PKCS#1 v1.5) no tiene componente aleatorio y los Id son fijos: la misma
/// operación, la misma dirección, el mismo certificado y la misma hora siempre producen el mismo sobre.
/// </para>
/// </remarks>
public static class DianSoapEnvelope
{
    /// <summary>Tipo de contenido HTTP de un mensaje SOAP 1.2.</summary>
    public const string ContentType = "application/soap+xml";

    /// <summary>
    /// Vida por defecto de la marca de tiempo: 60000 segundos (16 horas y 40 minutos), el mismo valor que usa ApiDian.
    /// </summary>
    public static readonly TimeSpan DefaultTimestampLifetime = TimeSpan.FromSeconds(60000);

    private const string SoapNs = "http://www.w3.org/2003/05/soap-envelope";
    private const string WcfNs = DianSoapRequest.ServiceNamespace;
    private const string WsaNs = "http://www.w3.org/2005/08/addressing";
    private const string WsseNs = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
    private const string WsuNs = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";
    private const string DsNs = SignedXml.XmlDsigNamespaceUrl;
    private const string ExcC14N = SignedXml.XmlDsigExcC14NTransformUrl;
    private const string RsaSha256 = SignedXml.XmlDsigRSASHA256Url;
    private const string Sha256 = SignedXml.XmlDsigSHA256Url;
    private const string Base64Binary =
        "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary";
    private const string X509v3 = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-x509-token-profile-1.0#X509v3";

    private const string ToId = "ID";
    private const string TimestampId = "TS";
    private const string TokenId = "X509-TOKEN";
    private const string SignatureId = "SIG";
    private const string KeyInfoId = "KI";
    private const string TokenReferenceId = "STR";

    // Prefijos cuyos espacios de nombres se incluyen siempre al canonicalizar, aunque el fragmento no los use.
    private const string SignedInfoPrefixes = "wsa soap wcf";
    private const string ReferencePrefixes = "soap wcf";

    /// <summary>
    /// Arma el sobre firmado de <paramref name="request"/> para el servicio en <paramref name="endpointUrl"/>
    /// y lo devuelve como texto XML (sin declaración XML), listo para enviarse en UTF-8.
    /// </summary>
    /// <param name="request">Operación y parámetros.</param>
    /// <param name="endpointUrl">Dirección absoluta del servicio de la DIAN; va tal cual en wsa:To.</param>
    /// <param name="certificate">Certificado del emisor con su clave privada RSA.</param>
    /// <param name="now">Hora actual; se escribe en UTC en wsu:Created.</param>
    /// <param name="timestampLifetime">
    /// Vida de la marca de tiempo (wsu:Expires = <paramref name="now"/> + vida); por defecto
    /// <see cref="DefaultTimestampLifetime"/>.
    /// </param>
    public static string Build(
        DianSoapRequest request,
        string endpointUrl,
        X509Certificate2 certificate,
        DateTimeOffset now,
        TimeSpan? timestampLifetime = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(endpointUrl);
        ArgumentNullException.ThrowIfNull(certificate);
        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("The endpoint must be an absolute HTTP or HTTPS URL.", nameof(endpointUrl));
        }

        var lifetime = timestampLifetime ?? DefaultTimestampLifetime;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero, nameof(timestampLifetime));

        using var rsa = certificate.GetRSAPrivateKey()
            ?? throw new ArgumentException("The certificate must carry an RSA private key.", nameof(certificate));

        var doc = Load(Render(request, endpointUrl, certificate, now, lifetime));

        // Primero la huella de wsa:To; después se firma ds:SignedInfo, que ya contiene esa huella.
        var to = (XmlElement)doc.GetElementsByTagName("To", WsaNs)[0]!;
        var signedInfo = (XmlElement)doc.GetElementsByTagName("SignedInfo", DsNs)[0]!;
        signedInfo.GetElementsByTagName("DigestValue", DsNs)[0]!.InnerText =
            Convert.ToBase64String(SHA256.HashData(CanonicalizeExclusive(to, ReferencePrefixes)));

        var signatureValue = rsa.SignData(
            CanonicalizeExclusive(signedInfo, SignedInfoPrefixes), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        doc.GetElementsByTagName("SignatureValue", DsNs)[0]!.InnerText = Convert.ToBase64String(signatureValue);

        return doc.OuterXml;
    }

    /// <summary>Escribe el sobre completo con la huella y el valor de la firma vacíos.</summary>
    private static string Render(DianSoapRequest request, string endpointUrl, X509Certificate2 certificate, DateTimeOffset now, TimeSpan lifetime)
    {
        var text = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = false,
            NewLineHandling = NewLineHandling.Entitize,
        };
        using (var w = XmlWriter.Create(text, settings))
        {
            w.WriteStartElement("soap", "Envelope", SoapNs);
            w.WriteAttributeString("xmlns", "soap", null, SoapNs);
            w.WriteAttributeString("xmlns", "wcf", null, WcfNs);

            w.WriteStartElement("soap", "Header", SoapNs);
            w.WriteAttributeString("xmlns", "wsa", null, WsaNs);
            WriteSecurity(w, certificate, now, lifetime);
            w.WriteElementString("wsa", "Action", WsaNs, request.Action);
            w.WriteStartElement("wsa", "To", WsaNs);
            w.WriteAttributeString("xmlns", "wsu", null, WsuNs);
            w.WriteAttributeString("wsu", "Id", WsuNs, ToId);
            w.WriteString(endpointUrl);
            w.WriteEndElement(); // To
            w.WriteEndElement(); // Header

            w.WriteStartElement("soap", "Body", SoapNs);
            w.WriteStartElement("wcf", request.OperationName, WcfNs);
            foreach (var (name, value) in request.Parameters)
            {
                w.WriteElementString("wcf", name, WcfNs, value);
            }

            w.WriteEndElement(); // operación
            w.WriteEndElement(); // Body
            w.WriteEndElement(); // Envelope
        }

        return text.ToString();
    }

    private static void WriteSecurity(XmlWriter w, X509Certificate2 certificate, DateTimeOffset now, TimeSpan lifetime)
    {
        w.WriteStartElement("wsse", "Security", WsseNs);
        w.WriteAttributeString("xmlns", "wsse", null, WsseNs);
        w.WriteAttributeString("xmlns", "wsu", null, WsuNs);

        w.WriteStartElement("wsu", "Timestamp", WsuNs);
        w.WriteAttributeString("wsu", "Id", WsuNs, TimestampId);
        w.WriteElementString("wsu", "Created", WsuNs, FormatUtc(now));
        w.WriteElementString("wsu", "Expires", WsuNs, FormatUtc(now + lifetime));
        w.WriteEndElement();

        // El certificado del emisor (DER, su forma binaria estándar) en Base64.
        w.WriteStartElement("wsse", "BinarySecurityToken", WsseNs);
        w.WriteAttributeString("EncodingType", Base64Binary);
        w.WriteAttributeString("ValueType", X509v3);
        w.WriteAttributeString("wsu", "Id", WsuNs, TokenId);
        w.WriteString(Convert.ToBase64String(certificate.RawData));
        w.WriteEndElement();

        WriteSignature(w);
        w.WriteEndElement(); // Security
    }

    private static void WriteSignature(XmlWriter w)
    {
        w.WriteStartElement("ds", "Signature", DsNs);
        w.WriteAttributeString("xmlns", "ds", null, DsNs);
        w.WriteAttributeString("Id", SignatureId);

        // ds:SignedInfo: los algoritmos y la huella de wsa:To; es el bloque que se firma.
        w.WriteStartElement("ds", "SignedInfo", DsNs);
        w.WriteStartElement("ds", "CanonicalizationMethod", DsNs);
        w.WriteAttributeString("Algorithm", ExcC14N);
        WriteInclusiveNamespaces(w, SignedInfoPrefixes);
        w.WriteEndElement();
        Algorithm(w, "SignatureMethod", RsaSha256);

        w.WriteStartElement("ds", "Reference", DsNs);
        w.WriteAttributeString("URI", "#" + ToId);
        w.WriteStartElement("ds", "Transforms", DsNs);
        w.WriteStartElement("ds", "Transform", DsNs);
        w.WriteAttributeString("Algorithm", ExcC14N);
        WriteInclusiveNamespaces(w, ReferencePrefixes);
        w.WriteEndElement(); // Transform
        w.WriteEndElement(); // Transforms
        Algorithm(w, "DigestMethod", Sha256);
        w.WriteStartElement("ds", "DigestValue", DsNs);
        w.WriteFullEndElement();
        w.WriteEndElement(); // Reference
        w.WriteEndElement(); // SignedInfo

        w.WriteStartElement("ds", "SignatureValue", DsNs);
        w.WriteFullEndElement();

        // ds:KeyInfo apunta al certificado del wsse:BinarySecurityToken en vez de repetirlo.
        w.WriteStartElement("ds", "KeyInfo", DsNs);
        w.WriteAttributeString("Id", KeyInfoId);
        w.WriteStartElement("wsse", "SecurityTokenReference", WsseNs);
        w.WriteAttributeString("wsu", "Id", WsuNs, TokenReferenceId);
        w.WriteStartElement("wsse", "Reference", WsseNs);
        w.WriteAttributeString("URI", "#" + TokenId);
        w.WriteAttributeString("ValueType", X509v3);
        w.WriteEndElement();
        w.WriteEndElement(); // SecurityTokenReference
        w.WriteEndElement(); // KeyInfo

        w.WriteEndElement(); // Signature
    }

    private static void WriteInclusiveNamespaces(XmlWriter w, string prefixList)
    {
        w.WriteStartElement("ec", "InclusiveNamespaces", ExcC14N);
        w.WriteAttributeString("xmlns", "ec", null, ExcC14N);
        w.WriteAttributeString("PrefixList", prefixList);
        w.WriteEndElement();
    }

    private static void Algorithm(XmlWriter w, string localName, string algorithm)
    {
        w.WriteStartElement("ds", localName, DsNs);
        w.WriteAttributeString("Algorithm", algorithm);
        w.WriteEndElement();
    }

    /// <summary>Fecha y hora en UTC con milisegundos y sufijo Z, por ejemplo 2026-11-15T15:30:45.123Z.</summary>
    private static string FormatUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static XmlDocument Load(string xml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(xml), XmlSigner.SafeReaderSettings);
        doc.Load(reader);
        return doc;
    }

    /// <summary>
    /// Canonicaliza un elemento con exc-c14n tal como queda dentro del sobre.
    /// </summary>
    /// <remarks>
    /// El elemento se copia a un documento propio junto con las declaraciones de espacios de nombres (xmlns:...)
    /// que hereda de sus ancestros, igual que lo hace un verificador de firmas XML. La canonicalización
    /// exclusiva solo escribe las que el elemento usa y las de <paramref name="prefixList"/>.
    /// </remarks>
    private static byte[] CanonicalizeExclusive(XmlElement element, string prefixList)
    {
        var doc = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        var copy = (XmlElement)doc.ImportNode(element, deep: true);
        doc.AppendChild(copy);
        for (var ancestor = element.ParentNode as XmlElement; ancestor is not null; ancestor = ancestor.ParentNode as XmlElement)
        {
            foreach (XmlAttribute attribute in ancestor.Attributes)
            {
                if (attribute.NamespaceURI == "http://www.w3.org/2000/xmlns/" && !copy.HasAttribute(attribute.Name))
                {
                    copy.SetAttributeNode((XmlAttribute)doc.ImportNode(attribute, deep: true));
                }
            }
        }

        var transform = new XmlDsigExcC14NTransform(includeComments: false, inclusiveNamespacesPrefixList: prefixList);
        transform.LoadInput(doc);
        using var output = (Stream)transform.GetOutput(typeof(Stream));
        using var buffer = new MemoryStream();
        output.CopyTo(buffer);
        return buffer.ToArray();
    }
}
