using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using ImpiFact.Dian.Soap;
using ImpiFact.Dian.Tests.Signing;

namespace ImpiFact.Dian.Tests.Soap;

// Pruebas del sobre SOAP (protocolo de mensajes XML del servicio web de la DIAN) firmado con WS-Security
// (estándar de seguridad para SOAP: marca de tiempo, certificado y firma en el encabezado). La firma cubre
// solo el elemento wsa:To de WS-Addressing (estándar que pone en el encabezado la acción y el destino).
public class DianSoapEnvelopeTests
{
    private const string SoapNs = "http://www.w3.org/2003/05/soap-envelope";
    private const string WcfNs = "http://wcf.dian.colombia";
    private const string WsaNs = "http://www.w3.org/2005/08/addressing";
    private const string WsseNs = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
    private const string WsuNs = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";
    private const string DsNs = "http://www.w3.org/2000/09/xmldsig#";
    private const string ExcC14N = "http://www.w3.org/2001/10/xml-exc-c14n#";
    private const string X509v3 = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-x509-token-profile-1.0#X509v3";
    private const string Base64Binary =
        "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary";

    private const string Endpoint = "https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc";

    // 15:30:45.123 UTC; el reloj se inyecta para que la marca de tiempo sea predecible.
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 15, 30, 45, 123, TimeSpan.Zero);

    private static readonly SendBillSyncRequest BillSync = new("SETP990000001.xml", "UEsDBAo=");

    public static IEnumerable<TestCaseData> AllOperations()
    {
        yield return new TestCaseData(BillSync, "SendBillSync").SetArgDisplayNames("SendBillSync");
        yield return new TestCaseData(new SendTestSetAsyncRequest("SETP990000001.xml", "UEsDBAo=", "test-set-id"), "SendTestSetAsync")
            .SetArgDisplayNames("SendTestSetAsync");
        yield return new TestCaseData(new GetStatusRequest("cufe-1"), "GetStatus").SetArgDisplayNames("GetStatus");
        yield return new TestCaseData(new GetStatusZipRequest("zip-key-1"), "GetStatusZip").SetArgDisplayNames("GetStatusZip");
        yield return new TestCaseData(new GetNumberingRangeRequest("900000001", "900000001", "software-1"), "GetNumberingRange")
            .SetArgDisplayNames("GetNumberingRange");
        yield return new TestCaseData(new GetAcquirerRequest("31", "900123456"), "GetAcquirer").SetArgDisplayNames("GetAcquirer");
    }

    [Test]
    public void Envelope_root_declares_soap12_and_wcf_namespaces()
    {
        var doc = Load(Build(BillSync));
        var root = doc.DocumentElement!;

        Assert.Multiple(() =>
        {
            Assert.That(root.LocalName, Is.EqualTo("Envelope"));
            Assert.That(root.NamespaceURI, Is.EqualTo(SoapNs));
            Assert.That(root.Prefix, Is.EqualTo("soap"));
            Assert.That(root.GetAttribute("xmlns:soap"), Is.EqualTo(SoapNs));
            Assert.That(root.GetAttribute("xmlns:wcf"), Is.EqualTo(WcfNs));
            Assert.That(ChildNames(root), Is.EqualTo(new[] { "soap:Header", "soap:Body" }));
        });
    }

    [Test]
    public void Header_declares_wsa_and_holds_security_action_and_to_in_order()
    {
        var header = Header(Load(Build(BillSync)));

        Assert.Multiple(() =>
        {
            Assert.That(header.GetAttribute("xmlns:wsa"), Is.EqualTo(WsaNs));
            Assert.That(ChildNames(header), Is.EqualTo(new[] { "wsse:Security", "wsa:Action", "wsa:To" }));
            Assert.That(header.ChildNodes[0]!.NamespaceURI, Is.EqualTo(WsseNs));
            Assert.That(((XmlElement)header.ChildNodes[0]!).GetAttribute("xmlns:wsu"), Is.EqualTo(WsuNs));
        });
    }

    [TestCaseSource(nameof(AllOperations))]
    public void Action_names_the_operation(DianSoapRequest request, string operation)
    {
        var action = Header(Load(Build(request))).ChildNodes[1]!;

        Assert.Multiple(() =>
        {
            Assert.That(action.NamespaceURI, Is.EqualTo(WsaNs));
            Assert.That(action.InnerText, Is.EqualTo("http://wcf.dian.colombia/IWcfDianCustomerServices/" + operation));
            Assert.That(request.OperationName, Is.EqualTo(operation));
            Assert.That(request.Action, Is.EqualTo(action.InnerText));
        });
    }

    [Test]
    public void To_carries_the_endpoint_url_and_the_signed_id()
    {
        var to = (XmlElement)Header(Load(Build(BillSync))).ChildNodes[2]!;

        Assert.Multiple(() =>
        {
            Assert.That(to.NamespaceURI, Is.EqualTo(WsaNs));
            Assert.That(to.InnerText, Is.EqualTo(Endpoint));
            Assert.That(to.GetAttribute("Id", WsuNs), Is.EqualTo("ID"));
        });
    }

    [Test]
    public void Security_holds_timestamp_token_and_signature_in_order()
    {
        var security = Security(Load(Build(BillSync)));

        Assert.That(ChildNames(security), Is.EqualTo(new[] { "wsu:Timestamp", "wsse:BinarySecurityToken", "ds:Signature" }));
    }

    [Test]
    public void Timestamp_uses_the_injected_clock_and_the_default_lifetime_of_60000_seconds()
    {
        var timestamp = (XmlElement)Security(Load(Build(BillSync))).ChildNodes[0]!;

        Assert.Multiple(() =>
        {
            Assert.That(timestamp.GetAttribute("Id", WsuNs), Is.EqualTo("TS"));
            Assert.That(ChildNames(timestamp), Is.EqualTo(new[] { "wsu:Created", "wsu:Expires" }));
            Assert.That(timestamp.ChildNodes[0]!.InnerText, Is.EqualTo("2026-11-15T15:30:45.123Z"));
            Assert.That(timestamp.ChildNodes[1]!.InnerText, Is.EqualTo("2026-11-16T08:10:45.123Z"));
            Assert.That(DianSoapEnvelope.DefaultTimestampLifetime, Is.EqualTo(TimeSpan.FromSeconds(60000)));
        });
    }

    [Test]
    public void Timestamp_is_written_in_utc_with_a_custom_lifetime()
    {
        var colombiaNow = Now.ToOffset(TimeSpan.FromHours(-5));
        var xml = DianSoapEnvelope.Build(BillSync, Endpoint, TestCertificates.Signer, colombiaNow, TimeSpan.FromMinutes(5));
        var timestamp = Security(Load(xml)).ChildNodes[0]!;

        Assert.Multiple(() =>
        {
            Assert.That(timestamp.ChildNodes[0]!.InnerText, Is.EqualTo("2026-11-15T15:30:45.123Z"));
            Assert.That(timestamp.ChildNodes[1]!.InnerText, Is.EqualTo("2026-11-15T15:35:45.123Z"));
        });
    }

    [Test]
    public void Non_positive_lifetime_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DianSoapEnvelope.Build(BillSync, Endpoint, TestCertificates.Signer, Now, TimeSpan.Zero));
    }

    [Test]
    public void Relative_endpoint_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            DianSoapEnvelope.Build(BillSync, "/WcfDianCustomerServices.svc", TestCertificates.Signer, Now));
    }

    [Test]
    public void Certificate_without_private_key_is_rejected()
    {
        using var publicOnly = X509CertificateLoader.LoadCertificate(TestCertificates.Signer.RawData);

        Assert.Throws<ArgumentException>(() => DianSoapEnvelope.Build(BillSync, Endpoint, publicOnly, Now));
    }

    [Test]
    public void Binary_security_token_carries_the_der_certificate()
    {
        var token = (XmlElement)Security(Load(Build(BillSync))).ChildNodes[1]!;

        Assert.Multiple(() =>
        {
            Assert.That(token.GetAttribute("Id", WsuNs), Is.EqualTo("X509-TOKEN"));
            Assert.That(token.GetAttribute("EncodingType"), Is.EqualTo(Base64Binary));
            Assert.That(token.GetAttribute("ValueType"), Is.EqualTo(X509v3));
            Assert.That(Convert.FromBase64String(token.InnerText), Is.EqualTo(TestCertificates.Signer.RawData));
        });
    }

    [Test]
    public void Signature_uses_exclusive_c14n_rsa_sha256_and_sha256_over_the_to_element_only()
    {
        var ns = Namespaces(Load(Build(BillSync)), out var doc);
        var signature = (XmlElement)doc.SelectSingleNode("//ds:Signature", ns)!;
        string Attr(string xpath, string name) => ((XmlElement)signature.SelectSingleNode(xpath, ns)!).GetAttribute(name);

        Assert.Multiple(() =>
        {
            Assert.That(signature.GetAttribute("Id"), Is.EqualTo("SIG"));
            Assert.That(ChildNames(signature), Is.EqualTo(new[] { "ds:SignedInfo", "ds:SignatureValue", "ds:KeyInfo" }));
            Assert.That(Attr("ds:SignedInfo/ds:CanonicalizationMethod", "Algorithm"), Is.EqualTo(ExcC14N));
            Assert.That(Attr("ds:SignedInfo/ds:CanonicalizationMethod/ec:InclusiveNamespaces", "PrefixList"), Is.EqualTo("wsa soap wcf"));
            Assert.That(Attr("ds:SignedInfo/ds:SignatureMethod", "Algorithm"), Is.EqualTo("http://www.w3.org/2001/04/xmldsig-more#rsa-sha256"));
            Assert.That(signature.SelectNodes("ds:SignedInfo/ds:Reference", ns)!.Count, Is.EqualTo(1));
            Assert.That(Attr("ds:SignedInfo/ds:Reference", "URI"), Is.EqualTo("#ID"));
            Assert.That(signature.SelectNodes("ds:SignedInfo/ds:Reference/ds:Transforms/ds:Transform", ns)!.Count, Is.EqualTo(1));
            Assert.That(Attr("ds:SignedInfo/ds:Reference/ds:Transforms/ds:Transform", "Algorithm"), Is.EqualTo(ExcC14N));
            Assert.That(Attr("ds:SignedInfo/ds:Reference/ds:Transforms/ds:Transform/ec:InclusiveNamespaces", "PrefixList"), Is.EqualTo("soap wcf"));
            Assert.That(Attr("ds:SignedInfo/ds:Reference/ds:DigestMethod", "Algorithm"), Is.EqualTo("http://www.w3.org/2001/04/xmlenc#sha256"));
        });
    }

    [Test]
    public void Key_info_references_the_binary_security_token()
    {
        var ns = Namespaces(Load(Build(BillSync)), out var doc);
        var keyInfo = (XmlElement)doc.SelectSingleNode("//ds:Signature/ds:KeyInfo", ns)!;
        var tokenReference = (XmlElement)keyInfo.SelectSingleNode("wsse:SecurityTokenReference", ns)!;
        var reference = (XmlElement)tokenReference.SelectSingleNode("wsse:Reference", ns)!;

        Assert.Multiple(() =>
        {
            Assert.That(keyInfo.GetAttribute("Id"), Is.EqualTo("KI"));
            Assert.That(tokenReference.GetAttribute("Id", WsuNs), Is.EqualTo("STR"));
            Assert.That(reference.GetAttribute("URI"), Is.EqualTo("#X509-TOKEN"));
            Assert.That(reference.GetAttribute("ValueType"), Is.EqualTo(X509v3));
        });
    }

    [TestCaseSource(nameof(AllOperations))]
    public void Signature_verifies_with_the_certificate_from_the_binary_security_token(DianSoapRequest request, string operation)
    {
        Assert.That(Verify(Build(request)), Is.True, operation);
    }

    [Test]
    public void Signature_verification_fails_when_the_to_text_is_altered()
    {
        var tampered = Build(BillSync).Replace(Endpoint, "https://vpfe.dian.gov.co/WcfDianCustomerServices.svc", StringComparison.Ordinal);

        Assert.That(Verify(tampered), Is.False);
    }

    [Test]
    public void Envelope_is_deterministic_for_the_same_input()
    {
        Assert.That(Build(BillSync), Is.EqualTo(Build(BillSync)));
    }

    [Test]
    public void Send_bill_sync_body_holds_file_name_and_content()
    {
        Assert.That(BodyChildren(BillSync), Is.EqualTo(new[] { ("fileName", "SETP990000001.xml"), ("contentFile", "UEsDBAo=") }));
    }

    [Test]
    public void Send_test_set_async_body_adds_the_test_set_id()
    {
        var request = new SendTestSetAsyncRequest("SETP990000001.xml", "UEsDBAo=", "test-set-id");

        Assert.That(BodyChildren(request), Is.EqualTo(new[]
        {
            ("fileName", "SETP990000001.xml"), ("contentFile", "UEsDBAo="), ("testSetId", "test-set-id"),
        }));
    }

    [Test]
    public void Get_status_bodies_hold_the_track_id()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BodyChildren(new GetStatusRequest("cufe-1")), Is.EqualTo(new[] { ("trackId", "cufe-1") }));
            Assert.That(BodyChildren(new GetStatusZipRequest("zip-key-1")), Is.EqualTo(new[] { ("trackId", "zip-key-1") }));
        });
    }

    [Test]
    public void Get_acquirer_body_holds_the_identification_type_code_and_number_with_the_php_action()
    {
        var request = new GetAcquirerRequest("31", "900123456");

        Assert.Multiple(() =>
        {
            Assert.That(BodyChildren(request), Is.EqualTo(new[] { ("identificationType", "31"), ("identificationNumber", "900123456") }));
            Assert.That(request.Action, Is.EqualTo("http://wcf.dian.colombia/IWcfDianCustomerServices/GetAcquirer"));
        });
    }

    [Test]
    public void Get_numbering_range_body_holds_account_codes_and_software_code()
    {
        var request = new GetNumberingRangeRequest("900000001", "800000002", "software-1");

        Assert.That(BodyChildren(request), Is.EqualTo(new[]
        {
            ("accountCode", "900000001"), ("accountCodeT", "800000002"), ("softwareCode", "software-1"),
        }));
    }

    [TestCaseSource(nameof(AllOperations))]
    public void Body_operation_element_is_in_the_wcf_namespace(DianSoapRequest request, string operation)
    {
        var body = Load(Build(request)).DocumentElement!.ChildNodes[1]!;
        var operationElement = body.ChildNodes.Cast<XmlNode>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(operationElement.Name, Is.EqualTo("wcf:" + operation));
            Assert.That(operationElement.NamespaceURI, Is.EqualTo(WcfNs));
            Assert.That(operationElement.ChildNodes.Cast<XmlNode>().All(n => n.NamespaceURI == WcfNs && n.Prefix == "wcf"), Is.True);
        });
    }

    [Test]
    public void Body_text_is_escaped()
    {
        var request = new GetStatusRequest("a<b&c");

        Assert.That(BodyChildren(request), Is.EqualTo(new[] { ("trackId", "a<b&c") }));
    }

    [Test]
    public void Content_type_is_soap12()
    {
        Assert.That(DianSoapEnvelope.ContentType, Is.EqualTo("application/soap+xml"));
    }

    private static string Build(DianSoapRequest request) =>
        DianSoapEnvelope.Build(request, Endpoint, TestCertificates.Signer, Now);

    private static XmlDocument Load(string xml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        doc.LoadXml(xml);
        return doc;
    }

    private static XmlNamespaceManager Namespaces(XmlDocument loaded, out XmlDocument doc)
    {
        doc = loaded;
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("ds", DsNs);
        ns.AddNamespace("ec", ExcC14N);
        ns.AddNamespace("wsse", WsseNs);
        ns.AddNamespace("wsu", WsuNs);
        return ns;
    }

    private static XmlElement Header(XmlDocument doc) => (XmlElement)doc.DocumentElement!.ChildNodes[0]!;

    private static XmlElement Security(XmlDocument doc) => (XmlElement)Header(doc).ChildNodes[0]!;

    private static string[] ChildNames(XmlNode node) =>
        node.ChildNodes.Cast<XmlNode>().Where(n => n.NodeType == XmlNodeType.Element).Select(n => n.Name).ToArray();

    private static (string, string)[] BodyChildren(DianSoapRequest request)
    {
        var body = Load(Build(request)).DocumentElement!.ChildNodes[1]!;
        return body.FirstChild!.ChildNodes.Cast<XmlNode>().Select(n => (n.LocalName, n.InnerText)).ToArray();
    }

    // Verificación independiente con SignedXml de .NET: el certificado sale del BinarySecurityToken del propio
    // sobre, y la referencia "#ID" se resuelve por el atributo wsu:Id.
    private static bool Verify(string envelope)
    {
        var ns = Namespaces(Load(envelope), out var doc);
        var token = doc.SelectSingleNode("//wsse:BinarySecurityToken", ns)!;
        using var certificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(token.InnerText));
        var signature = (XmlElement)doc.SelectSingleNode("//ds:Signature", ns)!;

        // SignedXml se crea sobre el propio ds:Signature y no sobre el documento: al canonicalizar ds:SignedInfo,
        // SignedXml solo agrega las declaraciones xmlns heredadas del elemento con que se creó. Creado sobre el
        // documento, tomaría solo las de soap:Envelope y omitiría xmlns:wsa (declarado en soap:Header), aunque
        // la lista de prefijos "wsa soap wcf" exige escribirlo.
        var signedXml = new WsuIdSignedXml(signature);
        signedXml.LoadXml(signature);
        return signedXml.CheckSignature(certificate, verifySignatureOnly: true);
    }

    private sealed class WsuIdSignedXml(XmlElement signature) : SignedXml(signature)
    {
        public override XmlElement? GetIdElement(XmlDocument? document, string idValue)
        {
            var ns = new XmlNamespaceManager(document!.NameTable);
            ns.AddNamespace("wsu", WsuNs);
            return document.SelectSingleNode($"//*[@wsu:Id='{idValue}']", ns) as XmlElement
                ?? base.GetIdElement(document, idValue);
        }
    }
}
