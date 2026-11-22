using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImpiFact.Application.Abstractions;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Documents;
using ImpiFact.Integration.Tests.Companies;
using Microsoft.EntityFrameworkCore;

// Same namespace as the issue tests so that DocumentIssueHost (a namespace-scoped SetUpFixture) starts the API for it.
namespace ImpiFact.Integration.Tests.DocumentIssue;

/// <summary>
/// Support document and its adjustment note through the real API and PostgreSQL (phase 7a, T4):
/// <c>/api/v1/documents/support-documents</c> and <c>/support-documents/adjustment-notes</c>, their test-set routes and the
/// PHP aliases, the support-document software (stored encrypted, used to sign instead of the invoice one), the type 11
/// numbering, the <c>qty_docs_ds</c> quota, the listing by kind, the setup status and the isolation between companies.
/// </summary>
[Category("Integration")]
public sealed class SupportDocumentIssueApiTests
{
    private const string Route = "/api/v1/documents/support-documents";

    private const string NoteRoute = Route + "/adjustment-notes";

    private const string TestSetId = "2b9c6d1e-4f3a-4b8c-9d7e-1a2b3c4d5e6f";

    /// <summary>Support-document software of the golden capture (fictitious values, never real ones).</summary>
    private const string SupportSoftwareId = "7c1e5a90-2b4d-4f3a-8e6c-1d9b0a2f3c58";

    private const string SupportSoftwarePin = "54321";

    private static int planSequence = 1300;

    private static CompanyApiFactory Api => DocumentIssueHost.Api;

    /// <summary>The golden resolutions plus the SEDS (type 11) and NDS (type 13) ones of the Postman folder 01D.</summary>
    private static readonly object[] Resolutions =
    [
        .. IssuerSetup.GoldenResolutions,
        new Dictionary<string, object>
        {
            ["type_document_id"] = 11, ["prefix"] = "SEDS", ["resolution"] = "18760000001", ["resolution_date"] = "2022-01-01",
            ["from"] = 984000000, ["to"] = 985000000, ["date_from"] = "2022-01-01", ["date_to"] = "2030-01-19",
        },
        new Dictionary<string, object> { ["type_document_id"] = 13, ["prefix"] = "NDS", ["from"] = 1, ["to"] = 99999999 },
    ];

    [Test]
    public async Task A_support_document_is_issued_queued_and_signed_with_the_support_document_software()
    {
        using var issuer = await NewIssuerAsync();

        using var response = await PostAsync(issuer.Client, Route, Body("resident-seller"));

        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        Assert.That(root.GetProperty("cuds").GetString(), Has.Length.EqualTo(96));
        Assert.That(root.TryGetProperty("cufe", out _) || root.TryGetProperty("cude", out _), Is.False);
        var signed = Encoding.UTF8.GetString(Convert.FromBase64String(root.GetProperty("invoicexml").GetString()!));
        Assert.That(signed, Does.Contain($">{SupportSoftwareId}</sts:SoftwareID>"));
        Assert.That(signed, Does.Not.Contain(IssuerSetup.SoftwareId));
        Assert.That(signed, Does.Contain("schemeName=\"CUDS-SHA384\""));
        var document = await SingleDocumentAsync(issuer);
        Assert.That((document.DocumentTypeId, document.TypeOperationId, document.Status, document.UuidAlgorithm),
            Is.EqualTo((11, 23, DocumentStatus.Queued, "CUDS-SHA384")));
        Assert.That(document.CustomerSnapshot.IdentificationNumber, Is.EqualTo("800000003"));
    }

    [TestCase("/api/ubl2.1/support-document")]
    [TestCase("/api/ubl2.1/support-document/" + TestSetId)]
    [TestCase(Route + "/test-set/" + TestSetId)]
    public async Task The_other_support_document_routes_issue_it_too(string path)
    {
        using var issuer = await NewIssuerAsync();

        using var response = await PostAsync(issuer.Client, path, Body("non-resident-seller"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var document = await SingleDocumentAsync(issuer);
        Assert.That((document.DocumentTypeId, document.TypeOperationId), Is.EqualTo((11, 24)));
    }

    // php-rules 13.1, item 6: PUT config/softwaresupportdocument (and its /api/v1 twin) stores the support-document
    // software apart from the invoice one, with the PIN encrypted and never returned.
    [Test]
    public async Task The_support_document_software_is_stored_encrypted_by_both_routes()
    {
        using var issuer = await NewIssuerAsync(withSupportSoftware: false);

        using var alias = await issuer.Client.PutAsJsonAsync("/api/ubl2.1/config/softwaresupportdocument",
            new { identifier_support_document = SupportSoftwareId, pin_support_document = SupportSoftwarePin });
        var aliasText = await alias.Content.ReadAsStringAsync();
        Assert.That(alias.StatusCode, Is.EqualTo(HttpStatusCode.OK), aliasText);
        Assert.That(aliasText, Does.Not.Contain(SupportSoftwarePin));
        using var canonical = await issuer.Client.PutAsJsonAsync($"/api/v1/companies/{issuer.Nit}/software",
            new { idsupportdocument = SupportSoftwareId, pinsupportdocument = SupportSoftwarePin });
        Assert.That(canonical.StatusCode, Is.EqualTo(HttpStatusCode.OK), await canonical.Content.ReadAsStringAsync());

        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var rows = await CompanyApiTests.Rows<CompanySoftware>(scope.Db, issuer.CompanyId).OrderBy(row => row.DocumentKind).ToListAsync();
        Assert.That(rows.Select(row => (row.DocumentKind, row.TypeEnvironmentId, row.SoftwareIdentifier)), Is.EqualTo(new[]
        {
            (DocumentKind.Invoice, 2, IssuerSetup.SoftwareId), (DocumentKind.SupportDocument, 2, SupportSoftwareId),
        }));
        Assert.That(rows[1].PinCipher, Does.Not.Contain(SupportSoftwarePin));
    }

    [Test]
    public async Task Without_the_support_document_software_it_answers_422_and_stores_nothing()
    {
        using var issuer = await NewIssuerAsync(withSupportSoftware: false);

        using var response = await PostAsync(issuer.Client, Route, Body("resident-seller"));

        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), text);
        using var json = JsonDocument.Parse(text);
        Assert.That(json.RootElement.GetProperty("code").GetString(), Is.EqualTo("software.missing"));
        Assert.That(await DocumentsAsync(issuer), Is.Empty);
    }

    // php-rules 13.1, item 4: its own type 11 resolution and sequence; an invoice does not move it.
    [Test]
    public async Task The_support_document_has_its_own_numbering()
    {
        using var issuer = await NewIssuerAsync();
        var body = Edited("resident-seller", json => json.Remove("number"));

        using var invoice = await PostAsync(issuer.Client, "/api/v1/documents/invoices",
            Edited(Path.Combine("..", "invoice", "single-line-iva19"), json => json.Remove("number")));
        using var first = await PostAsync(issuer.Client, Route, body);
        using var second = await PostAsync(issuer.Client, Route, body);

        foreach (var response in new[] { invoice, first, second })
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        }

        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var numbers = await CompanyApiTests.Rows<Document>(scope.Db, issuer.CompanyId)
            .Where(row => row.DocumentTypeId == 11).OrderBy(row => row.Number).Select(row => row.FullNumber).ToListAsync();
        Assert.That(numbers, Is.EqualTo(new[] { "SEDS984000000", "SEDS984000001" }));
    }

    // php-rules 13.1, item 7: the support document consumes qty_docs_ds; the invoice quota does not limit it.
    [Test]
    public async Task An_exhausted_support_document_quota_refuses_it_but_not_the_invoice_nor_the_test_set()
    {
        using var issuer = await NewIssuerAsync();
        await AssignPlanAsync(issuer, invoices: 10, supportDocuments: 1, usedSupportDocuments: 1);

        using var refused = await PostAsync(issuer.Client, Route, Body("resident-seller"));
        using var testSet = await PostAsync(issuer.Client, $"{Route}/test-set/{TestSetId}", Body("resident-seller"));
        using var invoice = await PostAsync(issuer.Client, "/api/v1/documents/invoices", Body(Path.Combine("..", "invoice", "single-line-iva19")));

        var text = await refused.Content.ReadAsStringAsync();
        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), text);
        using var json = JsonDocument.Parse(text);
        Assert.That(json.RootElement.GetProperty("code").GetString(), Is.EqualTo("plan.limit_reached"));
        Assert.That(testSet.StatusCode, Is.EqualTo(HttpStatusCode.OK), await testSet.Content.ReadAsStringAsync());
        Assert.That(invoice.StatusCode, Is.EqualTo(HttpStatusCode.OK), await invoice.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task An_adjustment_note_to_an_accepted_support_document_of_the_company_is_issued()
    {
        using var issuer = await NewIssuerAsync();
        var (cuds, id) = await AcceptedSupportDocumentAsync(issuer);

        using var response = await PostAsync(issuer.Client, NoteRoute, Note(cuds));

        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
        using var json = JsonDocument.Parse(text);
        Assert.That(json.RootElement.GetProperty("cuds").GetString(), Has.Length.EqualTo(96));
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var note = await CompanyApiTests.Rows<Document>(scope.Db, issuer.CompanyId).SingleAsync(row => row.DocumentTypeId == 13);
        Assert.That((note.ReferenceDocumentId, note.TypeEnvironmentId, note.Status), Is.EqualTo(((Guid?)id, 2, DocumentStatus.Queued)));
    }

    [TestCase("/api/ubl2.1/sd-credit-note")]
    [TestCase("/api/ubl2.1/sd-credit-note/" + TestSetId)]
    [TestCase(NoteRoute + "/test-set/" + TestSetId)]
    public async Task The_other_adjustment_note_routes_issue_it_too(string path)
    {
        using var issuer = await NewIssuerAsync();
        var (cuds, _) = await AcceptedSupportDocumentAsync(issuer);

        using var response = await PostAsync(issuer.Client, path, Note(cuds));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
    }

    // php-rules 14, item 2: the referenced CUDS must be a support document of the company; another company's answers
    // exactly like one that does not exist, so nothing leaks.
    [Test]
    public async Task A_support_document_of_another_company_is_neither_visible_nor_referenceable()
    {
        using var first = await NewIssuerAsync();
        using var second = await NewIssuerAsync();
        var (cuds, id) = await AcceptedSupportDocumentAsync(first);

        using var foreign = await second.Client.GetAsync($"/api/v1/documents/{id}");
        using var foreignNote = await PostAsync(second.Client, NoteRoute, Note(cuds));
        using var unknownNote = await PostAsync(second.Client, NoteRoute, Note(new string('a', 96)));

        Assert.That(foreign.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var foreignText = await foreignNote.Content.ReadAsStringAsync();
        Assert.That(foreignNote.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), foreignText);
        using var json = JsonDocument.Parse(foreignText);
        Assert.That(json.RootElement.GetProperty("code").GetString(), Is.EqualTo("document.reference_not_found"));
        using var unknown = JsonDocument.Parse(await unknownNote.Content.ReadAsStringAsync());
        foreach (var field in new[] { "code", "message", "errors" })
        {
            Assert.That(json.RootElement.GetProperty(field).GetRawText(), Is.EqualTo(unknown.RootElement.GetProperty(field).GetRawText()),
                $"The {field} must not tell the two apart.");
        }
        Assert.That(await DocumentsAsync(second), Is.Empty);
    }

    [Test]
    public async Task The_document_list_filters_the_support_documents_by_kind()
    {
        using var issuer = await NewIssuerAsync();
        var (_, id) = await AcceptedSupportDocumentAsync(issuer);
        using (var invoice = await PostAsync(issuer.Client, "/api/v1/documents/invoices", Body(Path.Combine("..", "invoice", "single-line-iva19"))))
        {
            Assert.That(invoice.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        using var listed = await issuer.Client.GetAsync("/api/v1/documents?kind=support_document");
        using var one = await issuer.Client.GetAsync($"/api/v1/documents/{id}");

        var text = await listed.Content.ReadAsStringAsync();
        Assert.That(listed.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
        using var json = JsonDocument.Parse(text);
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.That(items.Select(item => item.GetProperty("id").GetGuid()), Is.EqualTo(new[] { id }));
        Assert.That(one.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task The_setup_status_reports_the_support_document_family()
    {
        using var issuer = await NewIssuerAsync();
        using var without = await NewIssuerAsync(withSupportSoftware: false, resolutions: IssuerSetup.GoldenResolutions);

        var ready = await SupportStatusAsync(issuer);
        var missing = await SupportStatusAsync(without);

        Assert.That(ready.GetProperty("type_environment_id").GetInt32(), Is.EqualTo(2));
        Assert.That(ready.GetProperty("software_configured").GetBoolean(), Is.True);
        Assert.That(Readiness(ready), Is.EqualTo(new[] { ("support_document", 11, true), ("support_document_adjustment_note", 13, true) }));
        Assert.That(missing.GetProperty("software_configured").GetBoolean(), Is.False);
        Assert.That(Readiness(missing), Is.EqualTo(new[] { ("support_document", 11, false), ("support_document_adjustment_note", 13, false) }));
    }

    private static async Task<JsonElement> SupportStatusAsync(Issuer issuer)
    {
        using var response = await issuer.Client.GetAsync($"/api/v1/companies/{issuer.Nit}/setup-status");
        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
        using var json = JsonDocument.Parse(text);
        return json.RootElement.GetProperty("support_document").Clone();
    }

    private static IEnumerable<(string, int, bool)> Readiness(JsonElement family) =>
        family.GetProperty("resolutions").EnumerateArray()
            .Select(row => (row.GetProperty("kind").GetString()!, row.GetProperty("type_document_id").GetInt32(), row.GetProperty("valid").GetBoolean()))
            .ToList();

    /// <summary>Issues the resident golden support document and marks it accepted by the DIAN; returns its CUDS and id.</summary>
    private static async Task<(string Cuds, Guid Id)> AcceptedSupportDocumentAsync(Issuer issuer)
    {
        using var response = await PostAsync(issuer.Client, Route, Body("resident-seller"));
        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
        using var json = JsonDocument.Parse(text);
        var cuds = json.RootElement.GetProperty("cuds").GetString()!;
        var id = json.RootElement.GetProperty("document").GetProperty("id").GetGuid();
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var document = await CompanyApiTests.Rows<Document>(scope.Db, issuer.CompanyId).AsTracking().SingleAsync(row => row.Id == id);
        document.MoveTo(DocumentStatus.Sent);
        document.MoveTo(DocumentStatus.Accepted);
        await scope.Db.SaveChangesAsync();
        return (cuds, id);
    }

    private static async Task<Issuer> NewIssuerAsync(bool withSupportSoftware = true, IEnumerable<object>? resolutions = null)
    {
        var (nit, dv) = CompaniesHost.NewNit();
        var issuer = await IssuerSetup.CreateAsync(nit, dv, resolutions: resolutions ?? Resolutions);
        if (withSupportSoftware)
        {
            using var software = await issuer.Client.PutAsJsonAsync($"/api/v1/companies/{nit}/software",
                new { idsupportdocument = SupportSoftwareId, pinsupportdocument = SupportSoftwarePin });
            Assert.That(software.StatusCode, Is.EqualTo(HttpStatusCode.OK), await software.Content.ReadAsStringAsync());
        }

        return issuer;
    }

    private static async Task AssignPlanAsync(Issuer issuer, long invoices, long supportDocuments, long usedSupportDocuments)
    {
        var planId = Interlocked.Increment(ref planSequence);
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        scope.Db.Add(new Plan { Id = planId, Name = $"Plan {planId}", Period = PlanPeriod.Package, IsActive = true });
        scope.Db.Add(new PlanQuota { PlanId = planId, DocumentKind = DocumentKind.Invoice, Quantity = invoices });
        scope.Db.Add(new PlanQuota { PlanId = planId, DocumentKind = DocumentKind.SupportDocument, Quantity = supportDocuments });
        var companyPlan = new CompanyPlan { CompanyId = issuer.CompanyId, PlanId = planId, StartsAt = Api.Clock.UtcNow.AddDays(-1), IsActive = true };
        scope.Db.Add(companyPlan);
        await scope.Db.SaveChangesAsync();
        scope.Db.Add(new PlanUsage { CompanyPlanId = companyPlan.Id, DocumentKind = DocumentKind.SupportDocument, Used = usedSupportDocuments });
        await scope.Db.SaveChangesAsync();
    }

    private static async Task<Document> SingleDocumentAsync(Issuer issuer)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        return await CompanyApiTests.Rows<Document>(scope.Db, issuer.CompanyId).SingleAsync();
    }

    private static async Task<List<DocumentStatus>> DocumentsAsync(Issuer issuer)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        return await CompanyApiTests.Rows<Document>(scope.Db, issuer.CompanyId).Select(row => row.Status).ToListAsync();
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, byte[] body)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await client.PostAsync(path, content);
    }

    /// <summary>The golden adjustment note pointing to <paramref name="cuds"/> (the golden one is the capture company's).</summary>
    private static byte[] Note(string cuds)
    {
        var node = JsonNode.Parse(File.ReadAllBytes(Path.Combine(GoldenIssueTests.GoldenRoot(), "sd-credit-note", "adjustment-with-reference", "input.json")))!
            .AsObject();
        node["billing_reference"]!["uuid"] = cuds;
        node.Remove("number");
        return Encoding.UTF8.GetBytes(node.ToJsonString());
    }

    /// <summary>input.json of a case of tests/golden/support-document (or a path relative to that folder).</summary>
    private static byte[] Body(string goldenCase) =>
        File.ReadAllBytes(Path.Combine(GoldenIssueTests.GoldenRoot(), "support-document", goldenCase, "input.json"));

    private static byte[] Edited(string goldenCase, Action<JsonObject> change)
    {
        var node = JsonNode.Parse(Body(goldenCase))!.AsObject();
        change(node);
        return Encoding.UTF8.GetBytes(node.ToJsonString());
    }
}
