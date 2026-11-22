using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Platform;
using ImpiFact.Integration.Tests.Companies;
using Microsoft.EntityFrameworkCore;

// Same namespace as the issue tests so that DocumentIssueHost (a namespace-scoped SetUpFixture) starts the API for it.
namespace ImpiFact.Integration.Tests.DocumentIssue;

/// <summary>
/// Contingency invoices through the real API and the database (phase 7a, T5; php-rules, section 12): type 03 on its own
/// type 3 resolution, answered with its <c>cude</c>; type 04 on the invoice resolution and consecutive, stored
/// <c>ContingencyPending</c> without any DIAN call; <c>send-pending</c>, which only touches the caller's company; the
/// per-company duplicate check and the invoice plan quota.
/// </summary>
[Category("Integration")]
public sealed class ContingencyInvoiceIssueApiTests
{
    public const string PaperBackupRoute = "/api/v1/documents/invoices/contingency";

    public const string DianContingencyRoute = "/api/v1/documents/invoices/dian-contingency";

    public const string SendPendingRoute = "/api/v1/documents/invoices/dian-contingency/send-pending";

    private const string TestSetId = "2b9c6d1e-4f3a-4b8c-9d7e-1a2b3c4d5e6f";

    private static int planSequence = 1700;

    private static CompanyApiFactory Api => DocumentIssueHost.Api;

    /// <summary>
    /// The golden resolutions plus the type 3 one of the Postman collection (folder 01), without its technical key: annex
    /// 1.9, section 11.1.4, says a contingency range has none.
    /// </summary>
    public static readonly object[] Resolutions =
    [
        .. IssuerSetup.GoldenResolutions,
        new Dictionary<string, object>
        {
            ["type_document_id"] = 3, ["prefix"] = "SETP", ["resolution"] = "18760000001", ["resolution_date"] = "2019-01-19",
            ["from"] = 990000000, ["to"] = 995000000, ["date_from"] = "2019-01-19", ["date_to"] = "2030-01-19",
        },
    ];

    [Test]
    public async Task A_type_03_is_issued_queued_with_its_cude_on_its_own_resolution()
    {
        using var issuer = await NewIssuerAsync();

        using var response = await PostAsync(issuer.Client, PaperBackupRoute, Body("invoice-contingency", "paper-backup-reference"));

        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
        using var json = JsonDocument.Parse(text);
        var document = await SingleDocumentAsync(issuer);
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var resolution = await CompanyApiTests.Rows<Resolution>(scope.Db, issuer.CompanyId).SingleAsync(row => row.Id == document.ResolutionId);
        var reference = await CompanyApiTests.Rows<DocumentReference>(scope.Db, issuer.CompanyId).SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(json.RootElement.GetProperty("cude").GetString(), Is.EqualTo(document.Cufe).And.Length.EqualTo(96));
            Assert.That(json.RootElement.TryGetProperty("cufe", out _), Is.False);
            Assert.That((document.DocumentTypeId, document.Status, document.UuidAlgorithm), Is.EqualTo((3, DocumentStatus.Queued, "CUDE-SHA384")));
            Assert.That(resolution.DocumentTypeId, Is.EqualTo(3));
            Assert.That((reference.Kind, reference.Number, reference.IssueDate),
                Is.EqualTo((DocumentReferenceKind.AdditionalDocumentReference, "CTG201", (DateOnly?)new DateOnly(2026, 11, 14))));
        });
    }

    [Test]
    public async Task The_type_03_test_set_route_issues_it_too()
    {
        using var issuer = await NewIssuerAsync();

        using var response = await PostAsync(issuer.Client, PaperBackupRoute + "/test-set/" + TestSetId,
            Body("invoice-contingency", "paper-backup-reference"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        Assert.That((await SingleDocumentAsync(issuer)).DocumentTypeId, Is.EqualTo(3));
    }

    [Test]
    public async Task A_type_03_without_its_paper_invoice_answers_422_and_stores_nothing()
    {
        using var issuer = await NewIssuerAsync();

        using var response = await PostAsync(issuer.Client, PaperBackupRoute,
            Edited("invoice-contingency", "paper-backup-reference", json => json.Remove("AdditionalDocumentReferenceID")));

        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), text);
        using var json = JsonDocument.Parse(text);
        Assert.That(json.RootElement.GetProperty("errors").GetProperty("AdditionalDocumentReferenceID")[0].GetString(),
            Is.EqualTo("El campo AdditionalDocumentReferenceID es obligatorio."));
        Assert.That(await DocumentsAsync(issuer), Is.Empty);
    }

    [Test]
    public async Task A_type_04_is_stored_pending_on_the_invoice_resolution_without_calling_the_dian()
    {
        using var issuer = await NewIssuerAsync();
        var requests = Api.Dian.Requests.Count;

        using var response = await PostAsync(issuer.Client, DianContingencyRoute, Body("invoice-contingency-4", "pending-transmission"));

        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
        using var json = JsonDocument.Parse(text);
        var document = await SingleDocumentAsync(issuer);
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var resolution = await CompanyApiTests.Rows<Resolution>(scope.Db, issuer.CompanyId).SingleAsync(row => row.Id == document.ResolutionId);
        var messages = await SendMessagesAsync(scope, issuer.CompanyId);
        Assert.Multiple(() =>
        {
            Assert.That(json.RootElement.GetProperty("cufe").GetString(), Is.EqualTo(document.Cufe));
            Assert.That(json.RootElement.GetProperty("document").GetProperty("status").GetString(), Is.EqualTo("ContingencyPending"));
            Assert.That((document.DocumentTypeId, document.Status, document.UuidAlgorithm), Is.EqualTo((12, DocumentStatus.ContingencyPending, "CUFE-SHA384")));
            Assert.That(resolution.DocumentTypeId, Is.EqualTo(1), "Annex 1.9, 12.2: the numbering of the sales invoice.");
            Assert.That(document.Extensions?["variant"]?.GetValue<string>(), Is.EqualTo("dian_contingency"));
            Assert.That(messages.Select(message => (DocumentOf(message), message.NextAttemptAt)),
                Is.EqualTo(new[] { (document.Id, Api.Clock.UtcNow.AddMinutes(30)) }), "The first automatic attempt waits 30 minutes.");
            Assert.That(Api.Dian.Requests, Has.Count.EqualTo(requests), "Issuing a type 04 never calls the DIAN.");
        });
    }

    // php-rules 12.2, item 2: the PHP looked the number up in every company and told the caller about other companies'
    // documents. ImpiFact checks only the company's own invoices, which share the type 04 numbering.
    [Test]
    public async Task A_type_04_cannot_reuse_a_number_of_the_company_invoices_but_another_company_can_use_it()
    {
        using var first = await NewIssuerAsync();
        using var second = await NewIssuerAsync();
        using var invoice = await PostAsync(first.Client, "/api/v1/documents/invoices",
            Edited("invoice", "single-line-iva19", json => json["number"] = 990000301));

        using var repeated = await PostAsync(first.Client, DianContingencyRoute, Body("invoice-contingency-4", "pending-transmission"));
        using var otherCompany = await PostAsync(second.Client, DianContingencyRoute, Body("invoice-contingency-4", "pending-transmission"));

        Assert.That(invoice.StatusCode, Is.EqualTo(HttpStatusCode.OK), await invoice.Content.ReadAsStringAsync());
        var text = await repeated.Content.ReadAsStringAsync();
        Assert.That(repeated.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), text);
        Assert.That(text, Does.Contain("document.number_in_use").And.Not.Contain(second.Nit));
        Assert.That(otherCompany.StatusCode, Is.EqualTo(HttpStatusCode.OK), await otherCompany.Content.ReadAsStringAsync());
    }

    // php-rules 9.2, item 12: both contingency invoices consume the invoice family of the plan quota.
    [TestCase(PaperBackupRoute, "invoice-contingency", "paper-backup-reference")]
    [TestCase(DianContingencyRoute, "invoice-contingency-4", "pending-transmission")]
    public async Task An_exhausted_invoice_package_refuses_a_contingency_invoice(string path, string type, string goldenCase)
    {
        using var issuer = await NewIssuerAsync();
        await AssignInvoicePlanAsync(issuer, quantity: 2, used: 2);

        using var response = await PostAsync(issuer.Client, path, Body(type, goldenCase));

        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), text);
        Assert.That(text, Does.Contain("plan.limit_reached"));
        Assert.That(await DocumentsAsync(issuer), Is.Empty);
    }

    [Test]
    public async Task Send_pending_queues_only_the_pending_type_04_of_the_caller_company_now()
    {
        using var first = await NewIssuerAsync();
        using var second = await NewIssuerAsync();
        using var own = await PostAsync(first.Client, DianContingencyRoute, Body("invoice-contingency-4", "pending-transmission"));
        using var foreign = await PostAsync(second.Client, DianContingencyRoute, Body("invoice-contingency-4", "pending-transmission"));
        var ownDocument = await SingleDocumentAsync(first);
        var foreignDocument = await SingleDocumentAsync(second);

        using var response = await first.Client.PostAsync(SendPendingRoute, null);

        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
        using var json = JsonDocument.Parse(text);
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var ownMessages = await SendMessagesAsync(scope, first.CompanyId);
        var foreignMessages = await SendMessagesAsync(scope, second.CompanyId);
        Assert.Multiple(() =>
        {
            Assert.That(json.RootElement.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(json.RootElement.GetProperty("message").GetString(), Is.EqualTo("Envios de documentos de contingencia tipo 4 realizados con exito."));
            Assert.That(json.RootElement.GetProperty("documents").EnumerateArray()
                    .Select(row => (row.GetProperty("id").GetGuid(), row.GetProperty("full_number").GetString(), row.GetProperty("status").GetString())),
                Is.EqualTo(new[] { (ownDocument.Id, (string?)"SETP990000301", (string?)"ContingencyPending") }));
            Assert.That(ownMessages.Select(message => message.NextAttemptAt),
                Is.EquivalentTo(new[] { Api.Clock.UtcNow.AddMinutes(30), Api.Clock.UtcNow }), "send-pending adds a message due now.");
            Assert.That(foreignMessages.Select(message => (DocumentOf(message), message.NextAttemptAt)),
                Is.EqualTo(new[] { (foreignDocument.Id, Api.Clock.UtcNow.AddMinutes(30)) }), "Another company's documents are not touched.");
        });
    }

    [TestCase(SendPendingRoute + "?prefix=SETP&number=990000301")]
    [TestCase(SendPendingRoute + "?prefix=SETP")]
    [TestCase(SendPendingRoute)]
    public async Task Send_pending_takes_the_prefix_and_number_of_the_query(string path)
    {
        using var issuer = await NewIssuerAsync();
        using var issued = await PostAsync(issuer.Client, DianContingencyRoute, Body("invoice-contingency-4", "pending-transmission"));

        using var response = await issuer.Client.PostAsync(path, null);
        using var other = await issuer.Client.PostAsync(SendPendingRoute + "?prefix=FV", null);

        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
        using var json = JsonDocument.Parse(text);
        using var otherJson = JsonDocument.Parse(await other.Content.ReadAsStringAsync());
        Assert.That(json.RootElement.GetProperty("documents").GetArrayLength(), Is.EqualTo(1));
        Assert.That(otherJson.RootElement.GetProperty("message").GetString(),
            Is.EqualTo("No existen registros de documentos de contingencia tipo 4 para realizar envios...."));
    }

    [Test]
    public async Task Send_pending_with_a_number_and_no_prefix_answers_422()
    {
        using var issuer = await NewIssuerAsync();

        using var response = await issuer.Client.PostAsync(SendPendingRoute + "?number=990000301", null);

        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), text);
        using var json = JsonDocument.Parse(text);
        Assert.That(json.RootElement.GetProperty("errors").GetProperty("prefix")[0].GetString(), Is.EqualTo(
            "Para hacer envios los envios pendientes debe al menos suministrar el prefijo de las facturas de contingencia tipo 4 que desea enviar...."));
    }

    public static async Task<Issuer> NewIssuerAsync()
    {
        var (nit, dv) = CompaniesHost.NewNit();
        return await IssuerSetup.CreateAsync(nit, dv, resolutions: Resolutions);
    }

    public static async Task<Document> SingleDocumentAsync(Issuer issuer)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        return await CompanyApiTests.Rows<Document>(scope.Db, issuer.CompanyId).SingleAsync();
    }

    public static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, byte[] body)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await client.PostAsync(path, content);
    }

    /// <summary>input.json of a golden case.</summary>
    public static byte[] Body(string type, string goldenCase) =>
        File.ReadAllBytes(Path.Combine(GoldenIssueTests.GoldenRoot(), type, goldenCase, "input.json"));

    public static byte[] Edited(string type, string goldenCase, Action<JsonObject> change)
    {
        var node = JsonNode.Parse(Body(type, goldenCase))!.AsObject();
        change(node);
        return Encoding.UTF8.GetBytes(node.ToJsonString());
    }

    public static async Task AssignInvoicePlanAsync(Issuer issuer, long quantity, long used)
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

    private static async Task<List<DocumentStatus>> DocumentsAsync(Issuer issuer)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        return await CompanyApiTests.Rows<Document>(scope.Db, issuer.CompanyId).Select(row => row.Status).ToListAsync();
    }

    private static async Task<List<OutboxMessage>> SendMessagesAsync(HostScope scope, Guid companyId) =>
        await scope.Db.Set<OutboxMessage>().AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Type == OutboxMessageTypes.SendDocumentToDian)
            .ToListAsync();

    private static Guid DocumentOf(OutboxMessage message) => message.Payload["document_id"]!.GetValue<Guid>();
}
