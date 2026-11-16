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
/// Signs a DIAN UBL 2.1 document (invoice, credit note, debit note) with XAdES-EPES as described
/// in Anexo Técnico 002 (signature policy v2): enveloped XMLDSig, inclusive C14N 1.0, RSA-SHA256,
/// SHA-256 digests, and references to the document, to ds:KeyInfo and to xades:SignedProperties.
/// </summary>
/// <remarks>
/// The signature is spliced into the text of the empty ext:UBLExtension[2]/ext:ExtensionContent,
/// so every byte outside the signature (CUFE included) stays as it was. Ids are derived from a hash
/// of the input, the signing time and the certificate, and RSA PKCS#1 v1.5 is deterministic: the
/// same input, certificate and time always produce the same output.
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

    internal static readonly XmlReaderSettings SafeReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    public static string Sign(string unsignedXml, X509Certificate2 certificate, DateTimeOffset signingTime)
    {
        ArgumentNullException.ThrowIfNull(unsignedXml);
        ArgumentNullException.ThrowIfNull(certificate);
        using var rsa = certificate.GetRSAPrivateKey()
            ?? throw new ArgumentException("The certificate must carry an RSA private key.", nameof(certificate));

        var slot = SignatureSlot.Find(unsignedXml);
        var template = new SignatureTemplate(unsignedXml, certificate, FormatSigningTime(signingTime));

        // The enveloped transform removes the signature, which leaves exactly the unsigned document.
        var documentDigest = Digest(Canonicalize(Load(unsignedXml)));

        var draft = Load(slot.Insert(template.Render(Digests.Empty, signatureValue: string.Empty)));
        var digests = new Digests(
            documentDigest,
            Digest(CanonicalizeSubtree(FindById(draft, template.KeyInfoId))),
            Digest(CanonicalizeSubtree(FindById(draft, template.SignedPropertiesId))));

        var withDigests = Load(slot.Insert(template.Render(digests, signatureValue: string.Empty)));
        var signedInfo = (XmlElement)FindById(withDigests, template.SignatureId)
            .GetElementsByTagName("SignedInfo", DsNs)[0]!;
        var signatureValue = rsa.SignData(CanonicalizeSubtree(signedInfo), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return slot.Insert(template.Render(digests, Convert.ToBase64String(signatureValue)));
    }

    /// <summary>xsd:dateTime in Colombian time with milliseconds, as ApiDian and the annex example write it.</summary>
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

    private static string Digest(byte[] canonical) => Convert.ToBase64String(SHA256.HashData(canonical));

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
    /// Inclusive C14N of an element as a document subset: the apex element carries every namespace
    /// in scope and every inherited xml:* attribute (xml:lang, xml:space...), so both are copied from
    /// its ancestors onto it before canonicalizing it on its own. Ancestors are walked from the
    /// nearest outwards, so the nearest declaration wins.
    /// </summary>
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

    private sealed record Digests(string Document, string KeyInfo, string SignedProperties)
    {
        public static readonly Digests Empty = new(string.Empty, string.Empty, string.Empty);
    }

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

            // XMLDSig types X509SerialNumber as xsd:integer, and the annex example writes it in decimal.
            // ApiDian sends "0x" hex and the DIAN accepts it today. [verificar] in phase 1b against the DIAN.
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

                w.WriteStartElement("ds", "SignedInfo", DsNs);
                Algorithm(w, "CanonicalizationMethod", C14N);
                Algorithm(w, "SignatureMethod", RsaSha256);

                w.WriteStartElement("ds", "Reference", DsNs);
                w.WriteAttributeString("Id", SignatureId + "-ref0");
                w.WriteAttributeString("URI", string.Empty);
                w.WriteStartElement("ds", "Transforms", DsNs);
                Algorithm(w, "Transform", Enveloped);
                w.WriteEndElement();
                DigestElements(w, digests.Document);
                w.WriteEndElement();

                w.WriteStartElement("ds", "Reference", DsNs);
                w.WriteAttributeString("URI", "#" + KeyInfoId);
                DigestElements(w, digests.KeyInfo);
                w.WriteEndElement();

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
            // Declared here, as in the annex example, so the signature does not depend on the root declaring xades.
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

        private static string DeriveUuid(string unsignedXml, X509Certificate2 certificate, string signingTime)
        {
            var seed = SHA256.HashData(Encoding.UTF8.GetBytes(unsignedXml + "\n" + signingTime + "\n"
                + Convert.ToHexString(SHA256.HashData(certificate.RawData))));
            return new Guid(seed.AsSpan(0, 16), bigEndian: true).ToString("D");
        }
    }
}
