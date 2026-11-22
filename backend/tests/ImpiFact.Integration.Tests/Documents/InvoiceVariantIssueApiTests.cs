using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImpiFact.Application.Abstractions;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;
using ImpiFact.Integration.Tests.Companies;
using Microsoft.EntityFrameworkCore;

// Same namespace as the issue tests so that DocumentIssueHost (a namespace-scoped SetUpFixture) starts the API for it.
namespace ImpiFact.Integration.Tests.DocumentIssue;

/// <summary>
/// AIU, mandate and freight transport invoices through the real API and PostgreSQL (phase 7a, T2): the
/// <c>/api/v1/documents/invoices/{variant}</c> routes, the fixed operation, the shared invoice
/// numbering, the invoice plan quota and the isolation between companies.
/// </summary>
[Category("Integration")]
public sealed class InvoiceVariantIssueApiTests
{
    private static int planSequence = 900;

    private static CompanyApiFactory Api => DocumentIssueHost.Api;

    private static readonly (string Route, string Golden, int Operation)[] Variants =
    [
        ("invoices/aiu", "invoice-aiu", 9),
        ("invoices/mandate", "invoice-mandate", 11),
        ("invoices/transport", "invoice-transport", 21),
    ];

    public static IEnumerable<TestCaseData> VariantCases() =>
        Variants.Select(variant => new TestCaseData(variant.Route, variant.Golden, variant.Operation).SetArgDisplayNames(variant.Golden));

    [TestCaseSource(nameof(VariantCases))]
    public async Task A_variant_is_issued_as_a_queued_type_1_invoice_with_its_operation(string route, string golden, int operation)
    {
        using var issuer = await NewIssuerAsync();

        using var response = await PostAsync(issuer.Client, $"/api/v1/documents/{route}", Body(golden));

        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
        using var json = JsonDocument.Parse(text);
        Assert.That(json.RootElement.GetProperty("cufe").GetString(), Has.Length.EqualTo(96));
        var document = await SingleDocumentAsync(issuer);
        Assert.That((document.DocumentTypeId, document.TypeOperationId, document.Status), Is.EqualTo((1, operation, DocumentStatus.Queued)));
        Assert.That(document.Extensions?["variant"]?.GetValue<string>(), Is.EqualTo(golden["invoice-".Length..]));
    }

    // php-rules 9.2, item 7: the PHP answered 500 to any type_operation_id; ImpiFact answers 422 to another operation.
    [Test]
    public async Task Another_operation_answers_422_and_stores_nothing()
    {
        using var issuer = await NewIssuerAsync();

        using var response = await PostAsync(issuer.Client, "/api/v1/documents/invoices/mandate",
            Edited("invoice-mandate", json => json["type_operation_id"] = 10));

        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), text);
        using var json = JsonDocument.Parse(text);
        Assert.That(json.RootElement.GetProperty("errors").GetProperty("type_operation_id")[0].GetString(), Is.EqualTo("type operation id es inválido."));
        Assert.That(await DocumentsAsync(issuer), Is.Empty);
    }

    // The variants take their number from the invoice resolution, after the standard invoices, and never repeat one.
    [Test]
    public async Task Variants_share_the_invoice_numbering()
    {
        using var issuer = await NewIssuerAsync();
        var standard = Edited(Path.Combine("invoice", "single-line-iva19"), json => json.Remove("number"));

        using var first = await PostAsync(issuer.Client, "/api/v1/documents/invoices", standard);
        using var second = await PostAsync(issuer.Client, "/api/v1/documents/invoices/aiu", Edited("invoice-aiu", json => json.Remove("number")));
        using var third = await PostAsync(issuer.Client, "/api/v1/documents/invoices/transport", Edited("invoice-transport", json => json.Remove("number")));
        using var repeated = await PostAsync(issuer.Client, "/api/v1/documents/invoices/mandate",
            Edited("invoice-mandate", json => json["number"] = 990000000));

        foreach (var response in new[] { first, second, third })
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        }

        Assert.That(repeated.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), await repeated.Content.ReadAsStringAsync());
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var numbers = await CompanyApiTests.Rows<Document>(scope.Db, issuer.CompanyId).OrderBy(row => row.Number)
            .Select(row => new { row.DocumentTypeId, row.Number }).ToListAsync();
        Assert.That(numbers.Select(row => row.Number), Is.EqualTo(new long[] { 990000000, 990000001, 990000002 }));
        Assert.That(numbers.Select(row => row.DocumentTypeId), Is.All.EqualTo(1));
    }

    // php-rules 9.2, item 12: the variants consume the invoice family of the plan quota.
    [TestCaseSource(nameof(VariantCases))]
    public async Task An_exhausted_invoice_package_refuses_the_variant(string route, string golden, int operation)
    {
        using var issuer = await NewIssuerAsync();
        await AssignInvoicePlanAsync(issuer, quantity: 2, used: 2);

        using var response = await PostAsync(issuer.Client, $"/api/v1/documents/{route}", Body(golden));

        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), text);
        using var json = JsonDocument.Parse(text);
        Assert.That(json.RootElement.GetProperty("code").GetString(), Is.EqualTo("plan.limit_reached"));
        Assert.That(await DocumentsAsync(issuer), Is.Empty);
    }

    [Test]
    public async Task A_variant_of_one_company_is_not_visible_to_another()
    {
        using var first = await NewIssuerAsync();
        using var second = await NewIssuerAsync();

        using var issued = await PostAsync(first.Client, "/api/v1/documents/invoices/aiu", Body("invoice-aiu"));
        using var sameNumber = await PostAsync(second.Client, "/api/v1/documents/invoices/aiu", Body("invoice-aiu"));
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
        return await IssuerSetup.CreateAsync(nit, dv);
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

    /// <summary>input.json of the first golden case of a folder (<c>invoice-aiu</c>) or of a case (<c>invoice/single-line-iva19</c>).</summary>
    private static byte[] Body(string golden)
    {
        var dir = Path.Combine(GoldenIssueTests.GoldenRoot(), golden);
        var caseDir = File.Exists(Path.Combine(dir, "input.json")) ? dir : Directory.EnumerateDirectories(dir).Order(StringComparer.Ordinal).First();
        return File.ReadAllBytes(Path.Combine(caseDir, "input.json"));
    }

    private static byte[] Edited(string golden, Action<JsonObject> change)
    {
        var node = JsonNode.Parse(Body(golden))!.AsObject();
        change(node);
        return Encoding.UTF8.GetBytes(node.ToJsonString());
    }
}
