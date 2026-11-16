using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using ImpiFact.Dian.Attached;
using ImpiFact.Dian.Signing;
using ImpiFact.Dian.Tests.Signing;
using static ImpiFact.Dian.Tests.Attached.AttachedDocumentSamples;

namespace ImpiFact.Dian.Tests.Attached;

// El AttachedDocument (contenedor electrónico) se firma con la misma firma XAdES que la factura (firma XML
// con los datos extra que exige la DIAN). A diferencia de la factura, su plantilla tiene un solo
// ext:UBLExtension, y la firma va en su ext:ExtensionContent.
public class AttachedDocumentSigningTests
{
    private const string DsNs = "http://www.w3.org/2000/09/xmldsig#";
    private const string ExtNs = "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2";
    private const string SignaturePath = "/*/ext:UBLExtensions/ext:UBLExtension/ext:ExtensionContent/ds:Signature";

    private static readonly DateTimeOffset SigningTime = new(2026, 11, 16, 14, 15, 32, TimeSpan.Zero);

    [Test]
    public void Signed_attached_document_passes_SignedXml_verification()
    {
        var signed = Sign(AttachedDocumentBuilder.Build(Invoice()));

        Assert.That(CheckSignature(signed, TestCertificates.Signer), Is.True);
    }

    [Test]
    public void Signature_goes_in_the_only_extension_content_and_nothing_else_changes()
    {
        var unsigned = AttachedDocumentBuilder.Build(Invoice());
        var signed = Sign(unsigned);
        var (doc, ns, signature) = LoadSigned(signed);

        Assert.That(signature, Is.Not.Null);
        Assert.That(doc.SelectNodes("/*/ext:UBLExtensions/ext:UBLExtension", ns), Has.Count.EqualTo(1));
        Assert.That(doc.SelectNodes("//ds:Signature", ns), Has.Count.EqualTo(1));

        var start = signed.IndexOf("<ds:Signature ", StringComparison.Ordinal);
        var end = signed.IndexOf("</ds:Signature>", StringComparison.Ordinal) + "</ds:Signature>".Length;
        Assert.That(signed.Remove(start, end - start).Replace("<ext:ExtensionContent></ext:ExtensionContent>",
            "<ext:ExtensionContent/>", StringComparison.Ordinal), Is.EqualTo(unsigned));
    }

    // La firma cubre también los documentos embebidos: cambiar un byte dentro del CDATA (texto que el lector
    // XML no interpreta) la invalida.
    [Test]
    public void Tampering_with_the_embedded_document_breaks_the_signature()
    {
        var signed = Sign(AttachedDocumentBuilder.Build(Invoice()));
        var tampered = signed.Replace("<cbc:ID>SETP990000002</cbc:ID>\n  <cbc:Note>", "<cbc:ID>SETP990000003</cbc:ID>\n  <cbc:Note>",
            StringComparison.Ordinal);

        Assert.That(tampered, Is.Not.EqualTo(signed));
        Assert.That(CheckSignature(tampered, TestCertificates.Signer), Is.False);
    }

    // Una sección CDATA partida por un "]]>" embebido sigue verificando.
    [Test]
    public void Embedded_cdata_terminator_still_verifies()
    {
        var input = Invoice() with { SignedDocumentXml = "<a><![CDATA[x]]></a>" };

        Assert.That(CheckSignature(Sign(AttachedDocumentBuilder.Build(input)), TestCertificates.Signer), Is.True);
    }

    private static string Sign(string unsigned) => XmlSigner.Sign(unsigned, TestCertificates.Signer, SigningTime);

    private static bool CheckSignature(string signedXml, X509Certificate2 certificate)
    {
        var (doc, _, signature) = LoadSigned(signedXml);
        var checker = new SignedXml(doc);
        checker.LoadXml(signature!);
        return checker.CheckSignature(certificate, verifySignatureOnly: true);
    }

    private static (XmlDocument Doc, XmlNamespaceManager Ns, XmlElement? Signature) LoadSigned(string signedXml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        using (var reader = XmlReader.Create(new StringReader(signedXml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
        {
            doc.Load(reader);
        }

        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("ds", DsNs);
        ns.AddNamespace("ext", ExtNs);
        return (doc, ns, doc.SelectSingleNode(SignaturePath, ns) as XmlElement);
    }
}
