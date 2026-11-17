using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using ImpiFact.Application.Features.Documents.Email;
using ImpiFact.Dian.Soap;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Tests.Features.Documents.Email;

/// <summary>
/// El AttachedDocument (contenedor electrónico que se entrega al comprador) que se arma al aceptar un documento: el
/// emisor y el comprador salen del propio XML firmado que aceptó la DIAN, así el contenedor dice exactamente lo mismo
/// que el documento.
/// </summary>
public sealed class AttachedDocumentFactoryTests
{
    private const string Cufe = "0ca39906d8c33e7b";

    private static readonly DateTimeOffset Now = new(2026, 3, 2, 13, 0, 0, TimeSpan.Zero);

    [Test]
    public void The_sender_and_the_receiver_are_read_from_the_party_tax_schemes_of_the_signed_document()
    {
        var (sender, receiver) = AttachedDocumentFactory.ReadParties(SignedInvoice(customerCheckDigit: "1"));

        Assert.Multiple(() =>
        {
            Assert.That(sender.Name, Is.EqualTo("EMPRESA SAS"));
            Assert.That(sender.IdentificationNumber, Is.EqualTo("900000001"));
            Assert.That(sender.CheckDigit, Is.EqualTo("2"));
            Assert.That(sender.IdentificationTypeCode, Is.EqualTo("31"));
            Assert.That(sender.LiabilityCode, Is.EqualTo("O-13;O-15"));
            Assert.That(sender.RegimeCode, Is.EqualTo("48"));
            Assert.That(sender.TaxSchemeCode, Is.EqualTo("01"));
            Assert.That(sender.TaxSchemeName, Is.EqualTo("IVA"));

            Assert.That(receiver.Name, Is.EqualTo("CLIENTE & HIJOS"));
            Assert.That(receiver.IdentificationNumber, Is.EqualTo("800000001"));
            Assert.That(receiver.CheckDigit, Is.EqualTo("1"));
            Assert.That(receiver.IdentificationTypeCode, Is.EqualTo("13"));
            Assert.That(receiver.OrganizationTypeCode, Is.EqualTo("2"));
            Assert.That(receiver.LiabilityCode, Is.EqualTo("R-99-PN"));
            Assert.That(receiver.RegimeCode, Is.EqualTo("49"));
            Assert.That(receiver.TaxSchemeCode, Is.EqualTo("ZZ"));
            Assert.That(receiver.TaxSchemeName, Is.EqualTo("No aplica"));
        });
    }

    [Test]
    public void A_party_without_check_digit_has_none() =>
        Assert.That(AttachedDocumentFactory.ReadParties(SignedInvoice(customerCheckDigit: null)).Receiver.CheckDigit, Is.Null);

    [Test]
    public void A_document_without_parties_cannot_be_contained() =>
        Assert.Throws<ArgumentException>(() => AttachedDocumentFactory.ReadParties("<Invoice xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:Invoice-2\"/>"));

    [Test]
    public void The_container_is_signed_carries_both_documents_and_verifies()
    {
        using var certificate = SelfSigned();
        var signedInvoice = SignedInvoice(customerCheckDigit: "1");

        var attached = AttachedDocumentFactory.BuildSigned(Document(), signedInvoice, Result(), certificate, Now);

        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(attached);
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("ad", "urn:oasis:names:specification:ubl:schema:xsd:AttachedDocument-2");
        ns.AddNamespace("cbc", "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2");
        ns.AddNamespace("cac", "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2");
        ns.AddNamespace("ds", "http://www.w3.org/2000/09/xmldsig#");
        var checker = new SignedXml(doc);
        checker.LoadXml((XmlElement)doc.SelectSingleNode("//ds:Signature", ns)!);
        Assert.Multiple(() =>
        {
            Assert.That(checker.CheckSignature(certificate, verifySignatureOnly: true), Is.True);
            Assert.That(doc.SelectSingleNode("/ad:AttachedDocument/cbc:ID", ns)?.InnerText, Is.EqualTo(Cufe));
            Assert.That(doc.SelectSingleNode("/ad:AttachedDocument/cbc:ParentDocumentID", ns)?.InnerText, Is.EqualTo("SETP990000001"));
            Assert.That(doc.SelectSingleNode("/ad:AttachedDocument/cbc:ProfileExecutionID", ns)?.InnerText, Is.EqualTo("2"));
            Assert.That(doc.SelectSingleNode("//cac:ReceiverParty//cbc:RegistrationName", ns)?.InnerText, Is.EqualTo("CLIENTE & HIJOS"));
            Assert.That(doc.SelectSingleNode("/ad:AttachedDocument/cac:Attachment//cbc:Description", ns)?.InnerText, Is.EqualTo(signedInvoice));
            Assert.That(doc.SelectSingleNode("//cac:ParentDocumentLineReference//cbc:Description", ns)?.InnerText, Is.EqualTo(ApplicationResponse));
            Assert.That(doc.SelectSingleNode("//cac:ResultOfVerification/cbc:ValidationDate", ns)?.InnerText, Is.EqualTo("2026-03-02"));
        });
    }

    [Test]
    public void The_key_the_DIAN_returned_wins_over_the_stored_one()
    {
        using var certificate = SelfSigned();

        var attached = AttachedDocumentFactory.BuildSigned(
            Document(), SignedInvoice(customerCheckDigit: "1"), Result() with { XmlDocumentKey = "from-dian" }, certificate, Now);

        Assert.That(attached, Does.Contain("<cbc:ID>from-dian</cbc:ID>"));
    }

    [Test]
    public void A_credit_note_is_contained_with_its_CUDE()
    {
        using var certificate = SelfSigned();

        var attached = AttachedDocumentFactory.BuildSigned(
            Document(documentTypeId: 4), SignedInvoice(customerCheckDigit: "1", root: "CreditNote"), Result(), certificate, Now);

        Assert.That(attached, Does.Contain("schemeName=\"CUDE-SHA384\""));
    }

    [Test]
    public void Without_the_application_response_there_is_no_container()
    {
        using var certificate = SelfSigned();

        Assert.Throws<ArgumentException>(() => AttachedDocumentFactory.BuildSigned(
            Document(), SignedInvoice(customerCheckDigit: "1"), Result() with { XmlBase64Bytes = null }, certificate, Now));
    }

    private const string ApplicationResponse =
        "<ApplicationResponse xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:ApplicationResponse-2\" " +
        "xmlns:cbc=\"urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2\">" +
        "<cbc:IssueDate>2026-03-02</cbc:IssueDate><cbc:IssueTime>08:00:05-05:00</cbc:IssueTime></ApplicationResponse>";

    private static DianResult Result() => new(
        IsValid: true, StatusCode: "00", StatusDescription: "Procesado Correctamente.", StatusMessage: null, ErrorMessages: [],
        XmlDocumentKey: null, XmlFileName: null, XmlBase64Bytes: Encoding.UTF8.GetBytes(ApplicationResponse), ZipKey: null);

    private static Document Document(int documentTypeId = 1) => new()
    {
        Id = Guid.CreateVersion7(),
        CompanyId = Guid.CreateVersion7(),
        DocumentTypeId = documentTypeId,
        TypeEnvironmentId = 2,
        Prefix = "SETP",
        Number = 990000001,
        FullNumber = "SETP990000001",
        Cufe = Cufe,
        CustomerSnapshot = new CustomerSnapshot { IdentificationNumber = "800000001", Name = "CLIENTE & HIJOS" },
        CreatedByType = ActorKind.ApiKey,
    };

    private static X509Certificate2 SelfSigned()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=ImpiFact Email Tests, C=CO", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(Now.AddDays(-1), Now.AddYears(1));
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx, "x"), "x");
    }

    internal static string SignedInvoice(string? customerCheckDigit, string root = "Invoice")
    {
        var schemeId = customerCheckDigit is null ? "" : $" schemeID=\"{customerCheckDigit}\"";
        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="no"?>
            <{root} xmlns="urn:oasis:names:specification:ubl:schema:xsd:{root}-2" xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2" xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
              <cbc:ID>SETP990000001</cbc:ID>
              <cac:AccountingSupplierParty>
                <cbc:AdditionalAccountID>1</cbc:AdditionalAccountID>
                <cac:Party>
                  <cac:PartyTaxScheme>
                    <cbc:RegistrationName>EMPRESA SAS</cbc:RegistrationName>
                    <cbc:CompanyID schemeAgencyID="195" schemeID="2" schemeName="31">900000001</cbc:CompanyID>
                    <cbc:TaxLevelCode listName="48">O-13;O-15</cbc:TaxLevelCode>
                    <cac:TaxScheme><cbc:ID>01</cbc:ID><cbc:Name>IVA</cbc:Name></cac:TaxScheme>
                  </cac:PartyTaxScheme>
                </cac:Party>
              </cac:AccountingSupplierParty>
              <cac:AccountingCustomerParty>
                <cbc:AdditionalAccountID>2</cbc:AdditionalAccountID>
                <cac:Party>
                  <cac:PartyTaxScheme>
                    <cbc:RegistrationName>CLIENTE &amp; HIJOS</cbc:RegistrationName>
                    <cbc:CompanyID schemeAgencyID="195"{schemeId} schemeName="13">800000001</cbc:CompanyID>
                    <cbc:TaxLevelCode listName="49">R-99-PN</cbc:TaxLevelCode>
                    <cac:TaxScheme><cbc:ID>ZZ</cbc:ID><cbc:Name>No aplica</cbc:Name></cac:TaxScheme>
                  </cac:PartyTaxScheme>
                </cac:Party>
              </cac:AccountingCustomerParty>
            </{root}>
            """;
    }
}
