using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImpiFact.Application.Abstractions;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Catalog;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;
using ImpiFact.Integration.Tests.Companies;
using Microsoft.EntityFrameworkCore;

// Same namespace as the issue tests so that DocumentIssueHost (a namespace-scoped SetUpFixture) starts the API for it.
namespace ImpiFact.Integration.Tests.DocumentIssue;

/// <summary>
/// Export invoice through the real API and PostgreSQL (phase 7a, T3): <c>/api/v1/documents/invoices/export</c>, its test
/// set route and the PHP aliases, the type 2 resolution and numbering, the invoice plan quota, the 422 answers where the
/// PHP crashed, the Incoterms of the seeded catalog and the isolation between companies.
/// </summary>
[Category("Integration")]
public sealed class ExportInvoiceIssueApiTests
{
    private const string Route = "/api/v1/documents/invoices/export";

    private const string TestSetId = "2b9c6d1e-4f3a-4b8c-9d7e-1a2b3c4d5e6f";

    private static int planSequence = 950;

    private static CompanyApiFactory Api => DocumentIssueHost.Api;

    /// <summary>The golden resolutions plus the type 2 one of the Postman collection (folder 01).</summary>
    private static readonly object[] Resolutions =
    [
        .. IssuerSetup.GoldenResolutions,
        new Dictionary<string, object>
        {
            ["type_document_id"] = 2, ["prefix"] = "SETP", ["resolution"] = "18760000001", ["resolution_date"] = "2019-01-19",
            ["technical_key"] = IssuerSetup.TechnicalKey, ["from"] = 990000000, ["to"] = 995000000, ["date_from"] = "2019-01-19",
            ["date_to"] = "2030-01-19",
        },
    ];

    [Test]
    public async Task An_export_is_issued_as_a_queued_type_2_invoice_with_operation_10()
    {
        using var issuer = await NewIssuerAsync();

        using var response = await PostAsync(issuer.Client, Route, Body("cop-cif-additional-data"));

        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
        using var json = JsonDocument.Parse(text);
        Assert.That(json.RootElement.GetProperty("cufe").GetString(), Has.Length.EqualTo(96));
        var document = await SingleDocumentAsync(issuer);
        Assert.That((document.DocumentTypeId, document.TypeOperationId, document.Status), Is.EqualTo((2, 10, DocumentStatus.Queued)));
        Assert.That(document.Extensions?["variant"]?.GetValue<string>(), Is.EqualTo("export"));
    }

    [TestCase("/api/ubl2.1/invoice-export")]
    [TestCase("/api/ubl2.1/invoice-export/" + TestSetId)]
    [TestCase(Route + "/test-set/" + TestSetId)]
    public async Task The_other_export_routes_issue_it_too(string path)
    {
        using var issuer = await NewIssuerAsync();

        using var response = await PostAsync(issuer.Client, path, Body("usd-fob"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        Assert.That((await SingleDocumentAsync(issuer)).DocumentTypeId, Is.EqualTo(2));
    }

    // The golden usd-fob case records the PHP answering 500 to an export without a complete k_supplement.
    [Test]
    public async Task An_incomplete_supplement_k_answers_422_and_stores_nothing()
    {
        using var issuer = await NewIssuerAsync();

        using var response = await PostAsync(issuer.Client, Route,
            Edited("cop-cif-additional-data", json => json["k_supplement"]!.AsObject().Remove("responsible_incharge")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), text);
        using var json = JsonDocument.Parse(text);
        Assert.That(json.RootElement.GetProperty("errors").GetProperty("k_supplement.responsible_incharge")[0].GetString(),
            Is.EqualTo("El campo k supplement.responsible incharge es obligatorio cuando k supplement está presente."));
        Assert.That(await DocumentsAsync(issuer), Is.Empty);
    }

    // php-rules 11, item 5: the PHP fetched the TRM online or wrote 4000.00; ImpiFact asks for it.
    [Test]
    public async Task A_foreign_currency_without_exchange_rate_answers_422()
    {
        using var issuer = await NewIssuerAsync();

        using var response = await PostAsync(issuer.Client, Route, Edited("usd-fob", json => json.Remove("calculationrate")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), text);
        using var json = JsonDocument.Parse(text);
        Assert.That(json.RootElement.GetProperty("errors").GetProperty("calculationrate")[0].GetString(), Does.StartWith(
            "El campo calculationrate es obligatorio cuando idcurrency es USD y no COP"));
        Assert.That(await DocumentsAsync(issuer), Is.Empty);
    }

    // The accepted Incoterms are the seeded incoterms catalog except DPU (php-rules, section 18, question 3).
    [Test]
    public async Task Every_incoterm_of_the_catalog_but_dpu_is_accepted()
    {
        using var issuer = await NewIssuerAsync();
        List<string> codes;
        await using (var scope = Api.OpenScope(null, null, ActorType.System))
        {
            codes = await scope.Db.Set<Incoterm>().OrderBy(row => row.Id).Select(row => row.Code).ToListAsync();
        }

        var refused = new List<string>();
        foreach (var code in codes)
        {
            using var response = await PostAsync(issuer.Client, Route, Edited("cop-cif-additional-data", json =>
            {
                json.Remove("number");
                json["deliveryterms"]!["loss_risk_responsibility_code"] = code;
            }));
            if (response.StatusCode != HttpStatusCode.OK)
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), await response.Content.ReadAsStringAsync());
                refused.Add(code);
            }
        }

        Assert.That(codes, Has.Count.EqualTo(12));
        Assert.That(refused, Is.EqualTo(new[] { "DPU" }));
    }

    // php-rules 11, item 1: the export has its own type 2 resolution and sequence; a standard invoice does not move it.
    [Test]
    public async Task The_export_has_its_own_numbering()
    {
        using var issuer = await NewIssuerAsync();
        var export = Edited("cop-cif-additional-data", json => json.Remove("number"));

        using var standard = await PostAsync(issuer.Client, "/api/v1/documents/invoices",
            Edited(Path.Combine("..", "invoice", "single-line-iva19"), json => json.Remove("number")));
        using var first = await PostAsync(issuer.Client, Route, export);
        using var second = await PostAsync(issuer.Client, Route, export);
        using var repeated = await PostAsync(issuer.Client, Route, Edited("cop-cif-additional-data", json => json["number"] = 990000000));

        foreach (var response in new[] { standard, first, second })
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        }

        Assert.That(repeated.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), await repeated.Content.ReadAsStringAsync());
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var numbers = await CompanyApiTests.Rows<Document>(scope.Db, issuer.CompanyId)
            .Where(row => row.DocumentTypeId == 2).OrderBy(row => row.Number).Select(row => row.Number).ToListAsync();
        Assert.That(numbers, Is.EqualTo(new long[] { 990000000, 990000001 }));
    }

    // php-rules 9.2, item 12: the export consumes the invoice family of the plan quota; the test set skips the plan.
    [Test]
    public async Task An_exhausted_invoice_package_refuses_the_export_but_not_its_test_set()
    {
        using var issuer = await NewIssuerAsync();
        await AssignInvoicePlanAsync(issuer, quantity: 2, used: 2);

        using var refused = await PostAsync(issuer.Client, Route, Body("cop-cif-additional-data"));
        using var testSet = await PostAsync(issuer.Client, $"{Route}/test-set/{TestSetId}", Body("cop-cif-additional-data"));

        var text = await refused.Content.ReadAsStringAsync();
        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), text);
        using var json = JsonDocument.Parse(text);
        Assert.That(json.RootElement.GetProperty("code").GetString(), Is.EqualTo("plan.limit_reached"));
        Assert.That(testSet.StatusCode, Is.EqualTo(HttpStatusCode.OK), await testSet.Content.ReadAsStringAsync());
        Assert.That(await DocumentsAsync(issuer), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task An_export_of_one_company_is_not_visible_to_another()
    {
        using var first = await NewIssuerAsync();
        using var second = await NewIssuerAsync();

        using var issued = await PostAsync(first.Client, Route, Body("cop-cif-additional-data"));
        using var sameNumber = await PostAsync(second.Client, Route, Body("cop-cif-additional-data"));
        Guid id;
        using (var json = JsonDocument.Parse(await issued.Content.ReadAsStringAsync()))
        {
            id = json.RootElement.GetProperty("document").GetProperty("id").GetGuid();
        }

        using var foreign = await second.Client.GetAsync($"/api/v1/documents/{id}");
        using var own = await first.Client.GetAsync($"/api/v1/documents/{id}");

        Assert.That(sameNumber.StatusCode, Is.EqualTo(HttpStatusCode.OK), "Another company may use the same prefix and number.");
        Assert.That(foreign.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(own.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await DocumentsAsync(first), Has.Count.EqualTo(1));
        Assert.That(await DocumentsAsync(second), Has.Count.EqualTo(1));
    }

    private static async Task<Issuer> NewIssuerAsync()
    {
        var (nit, dv) = CompaniesHost.NewNit();
        return await IssuerSetup.CreateAsync(nit, dv, resolutions: Resolutions);
    }

    private static async Task AssignInvoicePlanAsync(Issuer issuer, long quantity, long used)
    {
        var planId = Interlocked.Increment(ref planSequence);
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        scope.Db.Add(new Plan { Id = planId, Name = $"Plan {planId}", Period = PlanPeriod.Package, IsActive = true });
        scope.Db.Add(new PlanQuota { PlanId = planId, DocumentKind = DocumentKind.Invoice, Quantity = quantity });
        var companyPlan = new CompanyPlan { CompanyId = issuer.CompanyId, PlanId = planId, StartsAt = Api.Clock.UtcNow.AddDays(-1), IsActive = true };
        scope.Db.Add(companyPlan);
        await scope.Db.SaveChangesAsync();
        scope.Db.Add(new PlanUsage { CompanyPlanId = companyPlan.Id, DocumentKind = DocumentKind.Invoice, Used = used });
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

    /// <summary>input.json of a case of tests/golden/invoice-export (or a path relative to that folder).</summary>
    private static byte[] Body(string goldenCase) =>
        File.ReadAllBytes(Path.Combine(GoldenIssueTests.GoldenRoot(), "invoice-export", goldenCase, "input.json"));

    private static byte[] Edited(string goldenCase, Action<JsonObject> change)
    {
        var node = JsonNode.Parse(Body(goldenCase))!.AsObject();
        change(node);
        return Encoding.UTF8.GetBytes(node.ToJsonString());
    }
}
