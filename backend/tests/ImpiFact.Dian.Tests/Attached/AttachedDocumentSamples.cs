using System.Text;
using ImpiFact.Dian.Attached;
using ImpiFact.Dian.Cufe;
using ImpiFact.Dian.Ubl;

namespace ImpiFact.Dian.Tests.Attached;

/// <summary>
/// Datos de prueba para el AttachedDocument (contenedor electrónico: el XML que el emisor entrega al
/// comprador con el documento firmado y el ApplicationResponse, la respuesta de validación firmada por la
/// DIAN). Todo es ficticio.
/// </summary>
internal static class AttachedDocumentSamples
{
    public const string InvoiceCufe = "0ca39906d8c33e7b2b965be29afcd74fc6746835362bce580f26d49868bc80a5445bdb6c57bbc164484c112df5117cb3";

    // 14:15:31 UTC es 09:15:31 en hora de Colombia (UTC-5).
    public static readonly DateTimeOffset GeneratedAt = new(2026, 11, 16, 14, 15, 31, TimeSpan.Zero);

    public const string SignedInvoice =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>\n" +
        "<Invoice xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:Invoice-2\" " +
        "xmlns:cbc=\"urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2\">\n" +
        "  <cbc:ID>SETP990000002</cbc:ID>\n" +
        "  <cbc:Note>Tildes: Dirección, Ñandú &amp; \"comillas\"</cbc:Note>\n" +
        "</Invoice>\n";

    public const string ApplicationResponse =
        "<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"no\"?>\n" +
        "<ApplicationResponse xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:ApplicationResponse-2\" " +
        "xmlns:cbc=\"urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2\" " +
        "xmlns:cac=\"urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2\">\n" +
        "  <cbc:ID>9000000000000001</cbc:ID>\n" +
        "  <cbc:IssueDate>2026-11-16</cbc:IssueDate>\n" +
        "  <cbc:IssueTime>09:15:27-05:00</cbc:IssueTime>\n" +
        "  <cac:DocumentResponse>\n" +
        "    <cac:DocumentReference>\n" +
        "      <cbc:IssueDate>2026-11-01</cbc:IssueDate>\n" +
        "      <cbc:IssueTime>23:59:59-05:00</cbc:IssueTime>\n" +
        "    </cac:DocumentReference>\n" +
        "  </cac:DocumentResponse>\n" +
        "</ApplicationResponse>\n";

    public static InvoiceParty Sender => new()
    {
        OrganizationTypeCode = "1",
        IdentificationNumber = "900000001",
        CheckDigit = "2",
        IdentificationTypeCode = "31",
        Name = "EMPRESA DE PRUEBA SAS",
        RegimeCode = "48",
        LiabilityCode = "O-23",
        TaxSchemeCode = "01",
        TaxSchemeName = "IVA",
    };

    public static InvoiceParty Receiver => new()
    {
        OrganizationTypeCode = "2",
        IdentificationNumber = "1020304050",
        IdentificationTypeCode = "13",
        Name = "PERSONA & HIJOS <PRUEBA>",
        RegimeCode = "49",
        LiabilityCode = "R-99-PN",
        TaxSchemeCode = "ZZ",
        TaxSchemeName = "No aplica",
    };

    public static AttachedDocumentInput Invoice() => new()
    {
        Kind = AttachedDocumentKind.Invoice,
        Environment = DianEnvironment.Testing,
        DocumentKey = InvoiceCufe,
        DocumentNumber = "SETP990000002",
        GeneratedAt = GeneratedAt,
        Sender = Sender,
        Receiver = Receiver,
        SignedDocumentXml = SignedInvoice,
        ApplicationResponseXml = Encoding.UTF8.GetBytes(ApplicationResponse),
    };
}
