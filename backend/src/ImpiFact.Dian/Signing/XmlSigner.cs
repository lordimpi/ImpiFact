using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using ImpiFact.Dian.Cufe;

namespace ImpiFact.Dian.Signing;

/// <summary>
/// Firma digitalmente un documento UBL 2.1 de la DIAN (factura, nota crédito o nota débito) con el formato
/// XAdES-EPES que exige el Anexo Técnico 002 (política de firma v2).
/// </summary>
/// <remarks>
/// <para>
/// UBL 2.1 es el estándar XML de documentos comerciales que usa la DIAN. XAdES-EPES es una firma digital
/// XML (estándar XMLDSig) con datos adicionales: la hora de firma, el certificado del firmante y la
/// política de firma de la DIAN.
/// </para>
/// <para>
/// Cómo es la firma: va dentro del mismo documento que firma, y al calcular la huella del documento se
/// excluye la propia firma (transformación "enveloped"). Antes de calcular cada huella (hash SHA-256), el
/// XML se escribe de una forma estándar para que el mismo contenido produzca siempre los mismos bytes
/// (canonicalización C14N 1.0 inclusiva). La firma guarda tres huellas: la del documento, la de ds:KeyInfo
/// (el certificado del firmante) y la de xades:SignedProperties (hora, certificado y política); el bloque
/// que las contiene se firma con RSA-SHA256.
/// </para>
/// <para>
/// La firma se inserta como texto en el ext:ExtensionContent vacío del segundo ext:UBLExtension (el lugar
/// reservado para la firma), así que ningún otro byte del documento cambia, incluido el CUFE (código único
/// de la factura electrónica: un hash que identifica la factura ante la DIAN). Los Id de la firma se
/// derivan de un hash del documento, la hora de firma y el certificado, y la firma RSA que se usa (relleno
/// PKCS#1 v1.5) no tiene componente aleatorio: el mismo documento, el mismo certificado y la misma hora
/// siempre producen exactamente la misma salida.
/// </para>
/// </remarks>
public static class XmlSigner
{
    private const string DsNs = SignedXml.XmlDsigNamespaceUrl;
    private const string XadesNs = "http://uri.etsi.org/01903/v1.3.2#";
    private const string XmlnsNs = "http://www.w3.org/2000/xmlns/";
    private const string XmlNs = "http://www.w3.org/XML/1998/namespace";
    private const string C14N = SignedXml.XmlDsigC14NTransformUrl;
    private const string RsaSha256 = SignedXml.XmlDsigRSASHA256Url;
    private const string Sha256 = SignedXml.XmlDsigSHA256Url;
    private const string Enveloped = SignedXml.XmlDsigEnvelopedSignatureTransformUrl;
    private const string SignedPropertiesType = "http://uri.etsi.org/01903#SignedProperties";

    private static readonly TimeSpan ColombiaUtcOffset = TimeSpan.FromHours(-5);

    // Lectura segura del XML: no procesa DTD ni descarga recursos externos (evita ataques de entidades XML).
    internal static readonly XmlReaderSettings SafeReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    /// <summary>
    /// Firma <paramref name="unsignedXml"/> con la clave privada de <paramref name="certificate"/> y devuelve
    /// el XML firmado. <paramref name="signingTime"/> se escribe en hora de Colombia en xades:SigningTime.
    /// </summary>
    /// <remarks>
    /// El valor de la firma (ds:SignatureValue) se calcula sobre ds:SignedInfo, el bloque que contiene las
    /// tres huellas, así que primero hay que conocerlas. Por eso se trabaja en tres pasadas:
    /// <list type="number">
    /// <item>Se calcula la huella del documento sin firmar.</item>
    /// <item>Se inserta un borrador de la firma (huellas vacías) y se calculan las huellas de ds:KeyInfo y
    /// xades:SignedProperties tal como quedan dentro del documento.</item>
    /// <item>Se inserta la firma con las tres huellas, se firma su ds:SignedInfo con RSA-SHA256 y se
    /// inserta la versión final con ese valor.</item>
    /// </list>
    /// </remarks>
    public static string Sign(string unsignedXml, X509Certificate2 certificate, DateTimeOffset signingTime)
    {
        ArgumentNullException.ThrowIfNull(unsignedXml);
        ArgumentNullException.ThrowIfNull(certificate);
        using var rsa = certificate.GetRSAPrivateKey()
            ?? throw new ArgumentException("The certificate must carry an RSA private key.", nameof(certificate));

        var slot = SignatureSlot.Find(unsignedXml);
        var template = new SignatureTemplate(unsignedXml, certificate, FormatSigningTime(signingTime));

        // Pasada 1: huella del documento. Al verificar, la firma se excluye de esa huella (transformación
        // "enveloped"), y lo que queda es exactamente el documento sin firmar; por eso se calcula sobre él.
        var documentDigest = Digest(Canonicalize(Load(unsignedXml)));

        // Pasada 2: borrador de la firma dentro del documento, para calcular las huellas de KeyInfo y SignedProperties.
        var draft = Load(slot.Insert(template.Render(Digests.Empty, signatureValue: string.Empty)));
        var digests = new Digests(
            documentDigest,
            Digest(CanonicalizeSubtree(FindById(draft, template.KeyInfoId))),
            Digest(CanonicalizeSubtree(FindById(draft, template.SignedPropertiesId))));

        // Pasada 3: firma con las tres huellas; se firma su ds:SignedInfo y se inserta el resultado.
        var withDigests = Load(slot.Insert(template.Render(digests, signatureValue: string.Empty)));
        var signedInfo = (XmlElement)FindById(withDigests, template.SignatureId)
            .GetElementsByTagName("SignedInfo", DsNs)[0]!;
        var signatureValue = rsa.SignData(CanonicalizeSubtree(signedInfo), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return slot.Insert(template.Render(digests, Convert.ToBase64String(signatureValue)));
    }

    /// <summary>
    /// Fecha y hora de firma en hora de Colombia, con milisegundos y desfase (por ejemplo
    /// 2026-11-15T10:30:00.123-05:00), como lo escriben ApiDian (la implementación de referencia en PHP) y
    /// el ejemplo del anexo.
    /// </summary>
    private static string FormatSigningTime(DateTimeOffset signingTime) =>
        signingTime.ToOffset(ColombiaUtcOffset).ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture)
        + CufeCalculator.ColombiaOffset;

    private static XmlDocument Load(string xml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(xml), SafeReaderSettings);
        doc.Load(reader);
        return doc;
    }

    private static XmlElement FindById(XmlDocument doc, string id) =>
        doc.SelectSingleNode($"//*[@Id='{id}']") as XmlElement
        ?? throw new InvalidOperationException($"Element with Id '{id}' not found.");

    /// <summary>Huella (hash SHA-256) de los bytes canonicalizados, en Base64, como va en ds:DigestValue.</summary>
    private static string Digest(byte[] canonical) => Convert.ToBase64String(SHA256.HashData(canonical));

    /// <summary>
    /// Escribe el documento en forma canónica (C14N 1.0, sin comentarios): una forma estándar para que el
    /// mismo contenido produzca siempre los mismos bytes.
    /// </summary>
    private static byte[] Canonicalize(XmlDocument doc)
    {
        var transform = new XmlDsigC14NTransform(includeComments: false);
        transform.LoadInput(doc);
        using var output = (Stream)transform.GetOutput(typeof(Stream));
        using var buffer = new MemoryStream();
        output.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Canonicaliza un solo elemento (el elemento que se está firmando, raíz del fragmento) tal como queda
    /// dentro del documento.
    /// </summary>
    /// <remarks>
    /// En la variante inclusiva de la canonicalización (C14N), el elemento firmado debe llevar también las
    /// declaraciones de espacios de nombres (xmlns:...) y los atributos xml:* (xml:lang, xml:space...) que
    /// hereda de sus ancestros. Por eso se copian de los ancestros a una copia del elemento antes de
    /// canonicalizarla por separado. Los ancestros se recorren del más cercano hacia afuera, así que si dos
    /// declaran el mismo nombre gana la más cercana, igual que dentro del documento.
    /// </remarks>
    private static byte[] CanonicalizeSubtree(XmlElement element)
    {
        var doc = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        var copy = (XmlElement)doc.ImportNode(element, deep: true);
        doc.AppendChild(copy);
        for (var ancestor = element.ParentNode as XmlElement; ancestor is not null; ancestor = ancestor.ParentNode as XmlElement)
        {
            foreach (XmlAttribute attribute in ancestor.Attributes)
            {
                var inherited = attribute.NamespaceURI is XmlnsNs or XmlNs;
                if (inherited && !copy.HasAttribute(attribute.Name))
                {
                    copy.SetAttributeNode((XmlAttribute)doc.ImportNode(attribute, deep: true));
                }
            }
        }

        return Canonicalize(doc);
    }

    /// <summary>Las tres huellas que guarda la firma, en Base64.</summary>
    private sealed record Digests(string Document, string KeyInfo, string SignedProperties)
    {
        public static readonly Digests Empty = new(string.Empty, string.Empty, string.Empty);
    }

    /// <summary>
    /// Genera el texto del elemento ds:Signature. <see cref="XmlSigner.Sign"/> lo genera de nuevo en cada pasada,
    /// con las huellas y el valor de firma que se conocen hasta ese momento.
    /// </summary>
    private sealed class SignatureTemplate
    {
        private readonly string _certificate;
        private readonly string _certificateDigest;
        private readonly string _issuerName;
        private readonly string _serialNumber;
        private readonly string _signingTime;

        public SignatureTemplate(string unsignedXml, X509Certificate2 certificate, string signingTime)
        {
            _certificate = Convert.ToBase64String(certificate.RawData);
            _certificateDigest = Convert.ToBase64String(SHA256.HashData(certificate.RawData));
            _issuerName = DistinguishedNameFormatter.Format(certificate.IssuerName);

            // El estándar de firmas XML (XMLDSig) define X509SerialNumber (número de serie del certificado)
            // como un número entero, y el ejemplo del anexo lo escribe en decimal. ApiDian envía hexadecimal
            // con "0x" y la DIAN hoy lo acepta. [verificar] en la fase 1b contra la DIAN.
            _serialNumber = new BigInteger(certificate.SerialNumberBytes.Span, isUnsigned: true, isBigEndian: true)
                .ToString(CultureInfo.InvariantCulture);
            _signingTime = signingTime;

            SignatureId = "xmldsig-" + DeriveUuid(unsignedXml, certificate, signingTime);
        }

        public string SignatureId { get; }

        public string KeyInfoId => SignatureId + "-keyinfo";

        public string SignedPropertiesId => SignatureId + "-signedprops";

        public string Render(Digests digests, string signatureValue)
        {
            var text = new StringBuilder();
            var settings = new XmlWriterSettings
            {
                ConformanceLevel = ConformanceLevel.Fragment,
                OmitXmlDeclaration = true,
                Indent = false,
                NewLineHandling = NewLineHandling.Entitize,
            };
            using (var w = XmlWriter.Create(text, settings))
            {
                w.WriteStartElement("ds", "Signature", DsNs);
                w.WriteAttributeString("xmlns", "ds", null, DsNs);
                w.WriteAttributeString("Id", SignatureId);

                // ds:SignedInfo: los algoritmos usados y las tres huellas; es el bloque que se firma.
                w.WriteStartElement("ds", "SignedInfo", DsNs);
                Algorithm(w, "CanonicalizationMethod", C14N);
                Algorithm(w, "SignatureMethod", RsaSha256);

                // Huella del documento completo (URI vacío), excluyendo la propia firma.
                w.WriteStartElement("ds", "Reference", DsNs);
                w.WriteAttributeString("Id", SignatureId + "-ref0");
                w.WriteAttributeString("URI", string.Empty);
                w.WriteStartElement("ds", "Transforms", DsNs);
                Algorithm(w, "Transform", Enveloped);
                w.WriteEndElement();
                DigestElements(w, digests.Document);
                w.WriteEndElement();

                // Huella de ds:KeyInfo (el certificado del firmante).
                w.WriteStartElement("ds", "Reference", DsNs);
                w.WriteAttributeString("URI", "#" + KeyInfoId);
                DigestElements(w, digests.KeyInfo);
                w.WriteEndElement();

                // Huella de xades:SignedProperties (hora de firma, certificado y política).
                w.WriteStartElement("ds", "Reference", DsNs);
                w.WriteAttributeString("Type", SignedPropertiesType);
                w.WriteAttributeString("URI", "#" + SignedPropertiesId);
                DigestElements(w, digests.SignedProperties);
                w.WriteEndElement();
                w.WriteEndElement(); // SignedInfo

                w.WriteStartElement("ds", "SignatureValue", DsNs);
                w.WriteAttributeString("Id", SignatureId + "-sigvalue");
                w.WriteString(signatureValue);
                w.WriteFullEndElement();

                w.WriteStartElement("ds", "KeyInfo", DsNs);
                w.WriteAttributeString("Id", KeyInfoId);
                w.WriteStartElement("ds", "X509Data", DsNs);
                w.WriteElementString("ds", "X509Certificate", DsNs, _certificate);
                w.WriteEndElement();
                w.WriteEndElement();

                w.WriteStartElement("ds", "Object", DsNs);
                WriteQualifyingProperties(w);
                w.WriteEndElement();

                w.WriteEndElement(); // Signature
            }

            return text.ToString();
        }

        private void WriteQualifyingProperties(XmlWriter w)
        {
            // xades:QualifyingProperties contiene los datos adicionales de XAdES (hora, certificado, política, rol).
            // El prefijo xades se declara aquí, como en el ejemplo del anexo, para que la firma no dependa de
            // que el elemento raíz del documento lo declare.
            w.WriteStartElement("xades", "QualifyingProperties", XadesNs);
            w.WriteAttributeString("xmlns", "xades", null, XadesNs);
            w.WriteAttributeString("Target", "#" + SignatureId);
            w.WriteStartElement("xades", "SignedProperties", XadesNs);
            w.WriteAttributeString("Id", SignedPropertiesId);
            w.WriteStartElement("xades", "SignedSignatureProperties", XadesNs);

            w.WriteElementString("xades", "SigningTime", XadesNs, _signingTime);

            w.WriteStartElement("xades", "SigningCertificate", XadesNs);
            w.WriteStartElement("xades", "Cert", XadesNs);
            w.WriteStartElement("xades", "CertDigest", XadesNs);
            DigestElements(w, _certificateDigest);
            w.WriteEndElement();
            w.WriteStartElement("xades", "IssuerSerial", XadesNs);
            w.WriteElementString("ds", "X509IssuerName", DsNs, _issuerName);
            w.WriteElementString("ds", "X509SerialNumber", DsNs, _serialNumber);
            w.WriteEndElement();
            w.WriteEndElement(); // Cert
            w.WriteEndElement(); // SigningCertificate

            w.WriteStartElement("xades", "SignaturePolicyIdentifier", XadesNs);
            w.WriteStartElement("xades", "SignaturePolicyId", XadesNs);
            w.WriteStartElement("xades", "SigPolicyId", XadesNs);
            w.WriteElementString("xades", "Identifier", XadesNs, DianSignaturePolicy.Identifier);
            w.WriteElementString("xades", "Description", XadesNs, DianSignaturePolicy.Description);
            w.WriteEndElement();
            w.WriteStartElement("xades", "SigPolicyHash", XadesNs);
            Algorithm(w, "DigestMethod", DianSignaturePolicy.HashAlgorithm);
            w.WriteElementString("ds", "DigestValue", DsNs, DianSignaturePolicy.Hash);
            w.WriteEndElement();
            w.WriteEndElement(); // SignaturePolicyId
            w.WriteEndElement(); // SignaturePolicyIdentifier

            w.WriteStartElement("xades", "SignerRole", XadesNs);
            w.WriteStartElement("xades", "ClaimedRoles", XadesNs);
            w.WriteElementString("xades", "ClaimedRole", XadesNs, DianSignaturePolicy.SupplierRole);
            w.WriteEndElement();
            w.WriteEndElement();

            w.WriteEndElement(); // SignedSignatureProperties
            w.WriteEndElement(); // SignedProperties
            w.WriteEndElement(); // QualifyingProperties
        }

        private static void Algorithm(XmlWriter w, string localName, string algorithm)
        {
            w.WriteStartElement("ds", localName, DsNs);
            w.WriteAttributeString("Algorithm", algorithm);
            w.WriteEndElement();
        }

        private static void DigestElements(XmlWriter w, string digestValue)
        {
            Algorithm(w, "DigestMethod", Sha256);
            w.WriteStartElement("ds", "DigestValue", DsNs);
            w.WriteString(digestValue);
            w.WriteFullEndElement();
        }

        // Identificador con forma de UUID pero predecible: los primeros 16 bytes de un SHA-256 del documento,
        // la hora de firma y la huella del certificado. Así la misma entrada siempre da los mismos Id.
        private static string DeriveUuid(string unsignedXml, X509Certificate2 certificate, string signingTime)
        {
            var seed = SHA256.HashData(Encoding.UTF8.GetBytes(unsignedXml + "\n" + signingTime + "\n"
                + Convert.ToHexString(SHA256.HashData(certificate.RawData))));
            return new Guid(seed.AsSpan(0, 16), bigEndian: true).ToString("D");
        }
    }
}
