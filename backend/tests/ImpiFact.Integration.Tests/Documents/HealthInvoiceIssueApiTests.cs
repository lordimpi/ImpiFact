using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using ImpiFact.Application.Abstractions;
using ImpiFact.Domain.Documents;
using ImpiFact.Integration.Tests.Companies;
using Microsoft.EntityFrameworkCore;

// Same namespace as the issue tests so that DocumentIssueHost (a namespace-scoped SetUpFixture) starts the API for it.
namespace ImpiFact.Integration.Tests.DocumentIssue;

/// <summary>
/// Health-sector invoices without RIPS through the real API and PostgreSQL (phase 7a, T7; php-rules 17): the canonical
/// invoice route reads <c>health_fields</c>, signs after the health extension and rejects what the
/// Ministerio de Salud's Documento Técnico 2 v001 cannot represent.
/// </summary>
[Category("Integration")]
public sealed class HealthInvoiceIssueApiTests
{
    private const string ExtNs = "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2";

    private static CompanyApiFactory Api => DocumentIssueHost.Api;

    [Test]
    public async Task A_health_invoice_is_signed_after_the_health_extension()
    {
        using var issuer = await NewIssuerAsync();

        using var response = await PostAsync(issuer.Client, "/api/v1/documents/invoices", HealthInvoice());

        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
        using var json = JsonDocument.Parse(text);
        var documentId = json.RootElement.GetProperty("document").GetProperty("id").GetGuid();
        var stored = await GoldenIssueTests.StoredSignedXmlAsync(documentId);
        Assert.That(GoldenIssueTests.Verifies(stored), Is.True, "SignedXml verification of the stored XML.");
        var extensions = Extensions(stored);
        Assert.That(extensions, Has.Count.EqualTo(3));
        Assert.That(extensions[1]["ext:ExtensionContent"]!.InnerXml, Does.Contain("<Value>url www.minsalud.gov.co</Value>"));
        Assert.That(extensions[2]["ext:ExtensionContent"]!.FirstChild!.LocalName, Is.EqualTo("Signature"));
        Assert.That(stored, Does.Contain("<cbc:CustomizationID>SS-CUFE</cbc:CustomizationID>"));
        Assert.That(stored, Does.Contain("<cbc:ID schemeID=\"01\">1</cbc:ID>"));

        Assert.That(json.RootElement.GetProperty("cufe").GetString(), Has.Length.EqualTo(96));
        var document = await SingleDocumentAsync(issuer);
        Assert.That((document.DocumentTypeId, document.Status), Is.EqualTo((1, DocumentStatus.Queued)));
        Assert.That(document.Extensions?["variant"]?.GetValue<string>(), Is.EqualTo("health"));
        Assert.That(document.Extensions?["health_fields"]?["users_info"]?[0]?["provider_code"]?.GetValue<string>(), Is.EqualTo("1100100001"));
    }

    // The Postman case «Factura de salud por capita inicial» (golden capitation-without-users) on the invoice route: operation 5,
    // SS-Recaudo, carries the billing period but never the extension, so the signature stays in the second slot.
    [Test]
    public async Task The_invoice_route_issues_a_collection_invoice_without_the_extension()
    {
        using var issuer = await NewIssuerAsync();
        var body = Edited(Path.Combine("invoice-health", "capitation-without-users"), _ => { });

        using var response = await PostAsync(issuer.Client, "/api/v1/documents/invoices", body);

        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
        using var json = JsonDocument.Parse(text);
        var stored = await GoldenIssueTests.StoredSignedXmlAsync(json.RootElement.GetProperty("document").GetProperty("id").GetGuid());
        Assert.That(stored, Does.Contain("<cbc:CustomizationID>SS-Recaudo</cbc:CustomizationID>"));
        Assert.That(stored, Does.Contain("<cac:InvoicePeriod>\n    <cbc:StartDate>2026-11-01</cbc:StartDate>"));
        Assert.That(stored, Does.Not.Contain("CustomTagGeneral"));
        Assert.That(Extensions(stored), Has.Count.EqualTo(2));
        Assert.That(GoldenIssueTests.Verifies(stored), Is.True);
    }

    // The Postman case «Factura del sector salud» (golden two-users-prepaid) has two users with different payment methods and
    // coverages, the first one with a contract and a policy, and a prepaid payment of type 1: not representable under the
    // Documento Técnico 2 v001 (one Collection), so ImpiFact answers 422 field by field and stores nothing.
    [Test]
    public async Task Users_that_cannot_share_one_collection_answer_422_and_store_nothing()
    {
        using var issuer = await NewIssuerAsync();
        var body = Edited(Path.Combine("invoice-health", "two-users-prepaid"), _ => { });

        using var response = await PostAsync(issuer.Client, "/api/v1/documents/invoices", body);

        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), text);
        using var json = JsonDocument.Parse(text);
        var errors = json.RootElement.GetProperty("errors");
        Assert.That(errors.GetProperty("health_fields.users_info.0.policy_number")[0].GetString(), Does.Contain("no los dos"));
        Assert.That(errors.TryGetProperty("health_fields.users_info.1.health_coverage_id", out _), Is.True, text);
        Assert.That(errors.TryGetProperty("health_fields.users_info.1.health_contracting_payment_method_id", out _), Is.True, text);
        Assert.That(await CountDocumentsAsync(issuer), Is.Zero);
    }

    // Without health_fields the national invoice keeps its previous XML (the golden suite pins the bytes).
    [Test]
    public async Task An_invoice_without_health_fields_keeps_the_standard_operation()
    {
        using var issuer = await NewIssuerAsync();

        using var response = await PostAsync(issuer.Client, "/api/v1/documents/invoices", Edited(Path.Combine("invoice", "single-line-iva19"), _ => { }));

        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
        using var json = JsonDocument.Parse(text);
        var stored = await GoldenIssueTests.StoredSignedXmlAsync(json.RootElement.GetProperty("document").GetProperty("id").GetGuid());
        Assert.That(stored, Does.Contain("<cbc:CustomizationID>10</cbc:CustomizationID>"));
        Assert.That(stored, Does.Not.Contain("InvoicePeriod"));
        Assert.That((await SingleDocumentAsync(issuer)).Extensions, Is.Null);
    }

    private static byte[] HealthInvoice() => Edited(Path.Combine("invoice", "single-line-iva19"), json =>
    {
        json["health_fields"] = JsonNode.Parse("""
            {"invoice_period_start_date":"2026-11-01","invoice_period_end_date":"2026-11-14","health_type_operation_id":1,
             "users_info":[{"provider_code":"1100100001","health_contracting_payment_method_id":4,"health_coverage_id":16,
                            "contract_number":"CUCON-0001","co_payment":"3300.00"}]}
            """);
        json["prepaid_payment"] = JsonNode.Parse("""{"idpayment":"1","prepaid_payment_type_id":1,"paidamount":"3300.00","receiveddate":"2026-11-14"}""");
        json["legal_monetary_totals"]!["pre_paid_amount"] = "3300.00";
    });

    private static List<XmlElement> Extensions(string xml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        doc.LoadXml(xml);
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("ext", ExtNs);
        return doc.SelectNodes("/*/ext:UBLExtensions/ext:UBLExtension", ns)!.Cast<XmlElement>().ToList();
    }

    private static async Task<Issuer> NewIssuerAsync()
    {
        var (nit, dv) = CompaniesHost.NewNit();
        return await IssuerSetup.CreateAsync(nit, dv);
    }

    private static async Task<Document> SingleDocumentAsync(Issuer issuer)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        return await CompanyApiTests.Rows<Document>(scope.Db, issuer.CompanyId).SingleAsync();
    }

    private static async Task<int> CountDocumentsAsync(Issuer issuer)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        return await CompanyApiTests.Rows<Document>(scope.Db, issuer.CompanyId).CountAsync();
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, byte[] body)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await client.PostAsync(path, content);
    }

    private static byte[] Edited(string golden, Action<JsonObject> change)
    {
        var node = JsonNode.Parse(File.ReadAllBytes(Path.Combine(GoldenIssueTests.GoldenRoot(), golden, "input.json")))!.AsObject();
        change(node);
        return Encoding.UTF8.GetBytes(node.ToJsonString());
    }
}
