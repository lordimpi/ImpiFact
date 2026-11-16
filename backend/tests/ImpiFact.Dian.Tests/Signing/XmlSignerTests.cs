using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text.RegularExpressions;
using System.Xml;
using ImpiFact.Dian.Signing;

namespace ImpiFact.Dian.Tests.Signing;

public class XmlSignerTests
{
    private const string DsNs = "http://www.w3.org/2000/09/xmldsig#";
    private const string XadesNs = "http://uri.etsi.org/01903/v1.3.2#";
    private const string ExtNs = "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2";
    private const string SignaturePath = "/*/ext:UBLExtensions/ext:UBLExtension[2]/ext:ExtensionContent/ds:Signature";
    private const string SignedSignatureProperties =
        SignaturePath + "/ds:Object/xades:QualifyingProperties/xades:SignedProperties/xades:SignedSignatureProperties";

    // 10:30:45.123 en Colombia.
    private static readonly DateTimeOffset SigningTime = new(2026, 11, 15, 15, 30, 45, 123, TimeSpan.Zero);

    public static IEnumerable<TestCaseData> GoldenCases() =>
        GoldenFixtures.All().Select(c => new TestCaseData(c).SetName($"{c.DocumentType}/{c.Name}"));

    [TestCaseSource(nameof(GoldenCases))]
    public void Signed_golden_document_passes_SignedXml_verification(GoldenFixture fixture)
    {
        var signed = Sign(fixture.ExpectedXml);

        Assert.That(CheckSignature(signed, TestCertificates.Signer), Is.True);
    }

    [TestCaseSource(nameof(GoldenCases))]
    public void Signed_golden_document_is_unchanged_outside_the_signature(GoldenFixture fixture)
    {
        var signed = Sign(fixture.ExpectedXml);

        var start = signed.IndexOf("<ds:Signature ", StringComparison.Ordinal);
        var end = signed.IndexOf("</ds:Signature>", StringComparison.Ordinal) + "</ds:Signature>".Length;
        Assert.That(start, Is.GreaterThan(0));
        Assert.That(signed.Remove(start, end - start), Is.EqualTo(fixture.ExpectedXml));
    }

    [TestCaseSource(nameof(GoldenCases))]
    public void Tampering_with_an_amount_breaks_the_signature(GoldenFixture fixture)
    {
        var signed = Sign(fixture.ExpectedXml);
        var amount = Regex.Match(signed, "(<cbc:PayableAmount[^>]*>)([0-9.]+)(<)");
        Assert.That(amount.Success, Is.True);
        var value = amount.Groups[2].Value;
        var tamperedValue = value[..^1] + (value[^1] == '0' ? '1' : '0');
        var tampered = signed.Remove(amount.Groups[2].Index, value.Length).Insert(amount.Groups[2].Index, tamperedValue);

        Assert.That(CheckSignature(tampered, TestCertificates.Signer), Is.False);
    }

    [Test]
    public void Tampering_with_the_signing_time_breaks_the_signature()
    {
        var signed = Sign(SampleInvoice());
        var tampered = signed.Replace("2026-11-15T10:30:45.123-05:00", "2026-11-15T10:30:46.123-05:00", StringComparison.Ordinal);

        Assert.That(tampered, Is.Not.EqualTo(signed));
        Assert.That(CheckSignature(tampered, TestCertificates.Signer), Is.False);
    }

    [Test]
    public void Signature_does_not_verify_with_another_certificate()
    {
        var signed = Sign(SampleInvoice());
        using var other = TestCertificates.CreateSelfSigned(new X500DistinguishedName("CN=Other"));

        Assert.That(CheckSignature(signed, other), Is.False);
    }

    // La C14N inclusiva lleva los atributos xml:* heredados a un subárbol canonicalizado, no solo los
    // espacios de nombres.
    [TestCase("xml:lang=\"es\"")]
    [TestCase("xml:space=\"default\"")]
    public void Inherited_xml_attributes_on_the_root_still_verify(string attribute)
    {
        var unsigned = new Regex("<Invoice ").Replace(SampleInvoice(), $"<Invoice {attribute} ", 1);

        Assert.That(CheckSignature(Sign(unsigned), TestCertificates.Signer), Is.True);
    }

    [Test]
    public void Signature_is_placed_in_the_second_extension_content()
    {
        var (_, ns, signature) = LoadSigned(Sign(SampleInvoice()));

        Assert.That(signature, Is.Not.Null);
        Assert.That(signature!.ParentNode!.LocalName, Is.EqualTo("ExtensionContent"));
        Assert.That(signature.ParentNode.ChildNodes, Has.Count.EqualTo(1));
        Assert.That(signature.SelectNodes("../../../ext:UBLExtension", ns), Has.Count.EqualTo(2));
    }

    [Test]
    public void Signed_info_uses_inclusive_c14n_rsa_sha256_and_three_sha256_references()
    {
        var (_, ns, signature) = LoadSigned(Sign(SampleInvoice()));
        var id = signature!.GetAttribute("Id");

        Assert.That(Text(signature, "ds:SignedInfo/ds:CanonicalizationMethod/@Algorithm", ns),
            Is.EqualTo("http://www.w3.org/TR/2001/REC-xml-c14n-20010315"));
        Assert.That(Text(signature, "ds:SignedInfo/ds:SignatureMethod/@Algorithm", ns),
            Is.EqualTo("http://www.w3.org/2001/04/xmldsig-more#rsa-sha256"));

        var references = signature.SelectNodes("ds:SignedInfo/ds:Reference", ns)!.Cast<XmlElement>().ToList();
        Assert.That(references, Has.Count.EqualTo(3));
        Assert.That(references.Select(r => r.SelectSingleNode("ds:DigestMethod/@Algorithm", ns)!.Value),
            Is.All.EqualTo("http://www.w3.org/2001/04/xmlenc#sha256"));

        var document = references[0];
        Assert.That(document.GetAttribute("URI"), Is.EqualTo(""));
        Assert.That(document.GetAttribute("Id"), Is.EqualTo(id + "-ref0"));
        Assert.That(document.SelectNodes("ds:Transforms/ds:Transform/@Algorithm", ns)!.Cast<XmlNode>().Select(n => n.Value),
            Is.EqualTo(new[] { "http://www.w3.org/2000/09/xmldsig#enveloped-signature" }));

        var keyInfo = references[1];
        Assert.That(keyInfo.GetAttribute("URI"), Is.EqualTo("#" + id + "-keyinfo"));
        Assert.That(Text(signature, "ds:KeyInfo/@Id", ns), Is.EqualTo(id + "-keyinfo"));

        var signedProperties = references[2];
        Assert.That(signedProperties.GetAttribute("Type"), Is.EqualTo("http://uri.etsi.org/01903#SignedProperties"));
        Assert.That(signedProperties.GetAttribute("URI"), Is.EqualTo("#" + id + "-signedprops"));
        Assert.That(Text(signature, "ds:Object/xades:QualifyingProperties/xades:SignedProperties/@Id", ns),
            Is.EqualTo(id + "-signedprops"));

        Assert.That(id, Does.Match("^xmldsig-[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"));
        Assert.That(Text(signature, "ds:SignatureValue/@Id", ns), Is.EqualTo(id + "-sigvalue"));
        Assert.That(Text(signature, "ds:Object/xades:QualifyingProperties/@Target", ns), Is.EqualTo("#" + id));
    }

    [Test]
    public void Key_info_carries_the_signing_certificate()
    {
        var (_, ns, signature) = LoadSigned(Sign(SampleInvoice()));

        Assert.That(Text(signature!, "ds:KeyInfo/ds:X509Data/ds:X509Certificate", ns),
            Is.EqualTo(Convert.ToBase64String(TestCertificates.Signer.RawData)));
    }

    [Test]
    public void Xades_properties_carry_the_dian_values()
    {
        var certificate = TestCertificates.Signer;
        var (doc, ns, _) = LoadSigned(Sign(SampleInvoice()));
        var props = (XmlElement)doc.SelectSingleNode(SignedSignatureProperties, ns)!;

        Assert.That(Text(props, "xades:SigningTime", ns), Is.EqualTo("2026-11-15T10:30:45.123-05:00"));

        Assert.That(Text(props, "xades:SigningCertificate/xades:Cert/xades:CertDigest/ds:DigestMethod/@Algorithm", ns),
            Is.EqualTo("http://www.w3.org/2001/04/xmlenc#sha256"));
        Assert.That(Text(props, "xades:SigningCertificate/xades:Cert/xades:CertDigest/ds:DigestValue", ns),
            Is.EqualTo(Convert.ToBase64String(SHA256.HashData(certificate.RawData))));
        Assert.That(Text(props, "xades:SigningCertificate/xades:Cert/xades:IssuerSerial/ds:X509IssuerName", ns),
            Is.EqualTo("CN=ImpiFact Test Signer,O=ImpiFact Pruebas,C=CO"));
        Assert.That(Text(props, "xades:SigningCertificate/xades:Cert/xades:IssuerSerial/ds:X509SerialNumber", ns),
            Is.EqualTo(new BigInteger(certificate.SerialNumberBytes.Span, isUnsigned: true, isBigEndian: true).ToString()));

        const string policy = "xades:SignaturePolicyIdentifier/xades:SignaturePolicyId/";
        Assert.That(Text(props, policy + "xades:SigPolicyId/xades:Identifier", ns),
            Is.EqualTo("https://facturaelectronica.dian.gov.co/politicadefirma/v2/politicadefirmav2.pdf"));
        Assert.That(Text(props, policy + "xades:SigPolicyId/xades:Description", ns),
            Is.EqualTo("Política de firma para facturas electrónicas de la República de Colombia."));
        Assert.That(Text(props, policy + "xades:SigPolicyHash/ds:DigestMethod/@Algorithm", ns),
            Is.EqualTo("http://www.w3.org/2001/04/xmlenc#sha256"));
        Assert.That(Text(props, policy + "xades:SigPolicyHash/ds:DigestValue", ns),
            Is.EqualTo("dMoMvtcG5aIzgYo0tIsSQeVJBDnUnfSOfBpxXrmor0Y="));

        Assert.That(Text(props, "xades:SignerRole/xades:ClaimedRoles/xades:ClaimedRole", ns), Is.EqualTo("supplier"));
    }

    [Test]
    public void Issuer_name_follows_rfc_2253_with_escaping_and_hex_for_unnamed_attributes()
    {
        // El constructor codifica primero el último RDN agregado, así que RFC 2253 (primero el último RDN
        // codificado) lista primero C; "openssl x509 -nameopt RFC2253" imprime lo mismo, salvo que le pone
        // nombre al atributo de correo electrónico.
        var builder = new X500DistinguishedNameBuilder();
        builder.AddCountryOrRegion("CO");
        builder.AddOrganizationName("Pruebas, S.A.S.");
        builder.AddCommonName("Signer");
        builder.AddEmailAddress("a@b.co");
        using var certificate = TestCertificates.CreateSelfSigned(builder.Build());

        var (doc, ns, _) = LoadSigned(XmlSigner.Sign(SampleInvoice(), certificate, SigningTime));

        Assert.That(Text(doc.DocumentElement!, SignedSignatureProperties +
                "/xades:SigningCertificate/xades:Cert/xades:IssuerSerial/ds:X509IssuerName", ns),
            Is.EqualTo("C=CO,O=Pruebas\\, S.A.S.,CN=Signer,1.2.840.113549.1.9.1=#16066140622e636f"));
        Assert.That(CheckSignature(XmlSigner.Sign(SampleInvoice(), certificate, SigningTime), certificate), Is.True);
    }

    [Test]
    public void Signing_time_is_written_in_colombian_time_whatever_the_input_offset()
    {
        var madrid = SigningTime.ToOffset(TimeSpan.FromHours(1));
        var (doc, ns, _) = LoadSigned(XmlSigner.Sign(SampleInvoice(), TestCertificates.Signer, madrid));

        Assert.That(Text(doc.DocumentElement!, SignedSignatureProperties + "/xades:SigningTime", ns),
            Is.EqualTo("2026-11-15T10:30:45.123-05:00"));
    }

    [Test]
    public void Same_input_certificate_and_time_give_the_same_bytes()
    {
        Assert.That(Sign(SampleInvoice()), Is.EqualTo(Sign(SampleInvoice())));
    }

    [Test]
    public void A_different_signing_time_gives_different_ids_and_signature()
    {
        var first = Sign(SampleInvoice());
        var second = XmlSigner.Sign(SampleInvoice(), TestCertificates.Signer, SigningTime.AddSeconds(1));

        Assert.That(LoadSigned(second).Signature!.GetAttribute("Id"),
            Is.Not.EqualTo(LoadSigned(first).Signature!.GetAttribute("Id")));
    }

    [Test]
    public void Self_closing_signature_slot_is_accepted()
    {
        var unsigned = SampleInvoice().Replace(
            "<ext:ExtensionContent></ext:ExtensionContent>", "<ext:ExtensionContent/>", StringComparison.Ordinal);

        Assert.That(CheckSignature(XmlSigner.Sign(unsigned, TestCertificates.Signer, SigningTime), TestCertificates.Signer),
            Is.True);
    }

    [Test]
    public void Rejects_a_document_without_a_second_extension()
    {
        var unsigned = Regex.Replace(SampleInvoice(),
            @"\s*<ext:UBLExtension>\s*<ext:ExtensionContent></ext:ExtensionContent>\s*</ext:UBLExtension>", "");

        var error = Assert.Throws<InvalidOperationException>(() => XmlSigner.Sign(unsigned, TestCertificates.Signer, SigningTime));
        Assert.That(error!.Message, Does.Contain("UBLExtension[2]"));
    }

    [Test]
    public void Rejects_a_signature_slot_that_already_has_content()
    {
        var unsigned = SampleInvoice().Replace(
            "<ext:ExtensionContent></ext:ExtensionContent>", "<ext:ExtensionContent><x/></ext:ExtensionContent>",
            StringComparison.Ordinal);

        var error = Assert.Throws<InvalidOperationException>(() => XmlSigner.Sign(unsigned, TestCertificates.Signer, SigningTime));
        Assert.That(error!.Message, Does.Contain("must be empty"));
    }

    [Test]
    public void Rejects_a_certificate_without_an_rsa_private_key()
    {
        using var publicOnly = X509CertificateLoader.LoadCertificate(TestCertificates.Signer.RawData);

        var error = Assert.Throws<ArgumentException>(() => XmlSigner.Sign(SampleInvoice(), publicOnly, SigningTime));
        Assert.That(error!.ParamName, Is.EqualTo("certificate"));
    }

    private static string SampleInvoice() =>
        GoldenFixtures.All().Single(f => f.DocumentType == "invoice" && f.Name == "single-line-iva19").ExpectedXml;

    private static string Sign(string unsigned) => XmlSigner.Sign(unsigned, TestCertificates.Signer, SigningTime);

    private static bool CheckSignature(string signedXml, X509Certificate2 certificate)
    {
        var (doc, _, signature) = LoadSigned(signedXml);
        var signedXmlChecker = new SignedXml(doc);
        signedXmlChecker.LoadXml(signature!);
        return signedXmlChecker.CheckSignature(certificate, verifySignatureOnly: true);
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
        ns.AddNamespace("xades", XadesNs);
        ns.AddNamespace("ext", ExtNs);
        return (doc, ns, doc.SelectSingleNode(SignaturePath, ns) as XmlElement);
    }

    private static string? Text(XmlNode context, string xpath, XmlNamespaceManager ns)
    {
        var node = context.SelectSingleNode(xpath, ns);
        return node is XmlAttribute attribute ? attribute.Value : node?.InnerText;
    }
}
