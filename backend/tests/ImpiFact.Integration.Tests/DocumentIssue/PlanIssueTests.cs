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

namespace ImpiFact.Integration.Tests.DocumentIssue;

/// <summary>
/// Guardas de la emisión de la fase 4 por la API real y con PostgreSQL: el plan de la empresa (activo, en su periodo y
/// con documentos disponibles, con los mensajes del PHP) y, en las notas, que la factura de la empresa referenciada ya
/// esté aceptada por la DIAN.
/// </summary>
[Category("Integration")]
public sealed class PlanIssueTests
{
    private static int planSequence = 700;

    private static CompanyApiFactory Api => DocumentIssueHost.Api;

    private static string Invoice => Path.Combine(GoldenIssueTests.GoldenRoot(), "invoice", "single-line-iva19");

    [Test]
    public async Task An_invoice_within_its_plan_is_issued()
    {
        using var issuer = await NewIssuerAsync();
        await AssignPlanAsync(issuer, PlanPeriod.Monthly, quantity: 5, used: 4);

        using var response = await PostAsync(issuer.Client, "invoice", Body(Invoice));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        Assert.That(await DocumentsAsync(issuer), Is.EqualTo(new[] { DocumentStatus.Queued }));
    }

    [TestCase("invoice", "single-line-iva19")]
    [TestCase("credit-note", "without-reference-invoice-period")]
    [TestCase("debit-note", "price-adjustment-with-reference")]
    public async Task An_exhausted_package_refuses_invoices_and_notes_with_the_php_message(string route, string name)
    {
        using var issuer = await NewIssuerAsync();
        await AssignPlanAsync(issuer, PlanPeriod.Package, quantity: 3, used: 3);

        using var response = await PostAsync(issuer.Client, route, Body(Path.Combine(GoldenIssueTests.GoldenRoot(), route, name)));

        await AssertRefusedAsync(response, "plan.limit_reached",
            "La empresa ha llegado al limite de documentos del plan por paquetes, por favor renueve su membresia...");
        Assert.That(await DocumentsAsync(issuer), Is.Empty);
    }

    [Test]
    public async Task An_inactive_plan_refuses_the_issue()
    {
        using var issuer = await NewIssuerAsync();
        await AssignPlanAsync(issuer, PlanPeriod.Package, quantity: 100, used: 0, planActive: false);

        using var response = await PostAsync(issuer.Client, "invoice", Body(Invoice));

        await AssertRefusedAsync(response, "plan.inactive",
            "El plan en el que esta registrado la empresa se encuentra en el momento INACTIVO para enviar documentos electronicos...");
    }

    [Test]
    public async Task A_monthly_plan_past_its_month_refuses_the_issue_even_with_documents_left()
    {
        using var issuer = await NewIssuerAsync();
        await AssignPlanAsync(issuer, PlanPeriod.Monthly, quantity: 100, used: 0, startsAt: Api.Clock.UtcNow.AddMonths(-1));

        using var response = await PostAsync(issuer.Client, "invoice", Body(Invoice));

        await AssertRefusedAsync(response, "plan.limit_reached",
            "La empresa ha llegado al limite de tiempo/documentos del plan por mensualidad, por favor renueve su membresia...");
    }

    [Test]
    public async Task A_yearly_plan_past_its_year_refuses_the_issue()
    {
        using var issuer = await NewIssuerAsync();
        await AssignPlanAsync(issuer, PlanPeriod.Yearly, quantity: 100, used: 0, startsAt: Api.Clock.UtcNow.AddYears(-1));

        using var response = await PostAsync(issuer.Client, "invoice", Body(Invoice));

        await AssertRefusedAsync(response, "plan.limit_reached",
            "La empresa ha llegado al limite de tiempo/documentos del plan por anualidad, por favor renueve su membresia...");
    }

    // Sin plan contratado activo la empresa no tiene límites, como el plan por defecto del PHP.
    [Test]
    public async Task A_deactivated_company_plan_leaves_the_company_without_limits()
    {
        using var issuer = await NewIssuerAsync();
        await AssignPlanAsync(issuer, PlanPeriod.Package, quantity: 1, used: 1, assignmentActive: false);

        using var response = await PostAsync(issuer.Client, "invoice", Body(Invoice));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
    }

    // Como el testSetStore del PHP: el set de pruebas de habilitación no revisa el plan.
    [Test]
    public async Task The_test_set_route_does_not_check_the_plan()
    {
        using var issuer = await NewIssuerAsync();
        await AssignPlanAsync(issuer, PlanPeriod.Package, quantity: 1, used: 1);

        using var response = await PostAsync(issuer.Client, "invoice/c3a1f0e2-5b7d-4e8a-9f10-2b3c4d5e6f70", Body(Invoice));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
    }

    [TestCase("credit-note", "partial-return-with-reference")]
    [TestCase("debit-note", "price-adjustment-with-reference")]
    public async Task A_note_waits_for_its_company_invoice_to_be_accepted(string route, string name)
    {
        using var issuer = await NewIssuerAsync();
        using var invoice = await PostAsync(issuer.Client, "invoice", Body(Invoice));
        string cufe;
        Guid invoiceId;
        using (var json = JsonDocument.Parse(await invoice.Content.ReadAsStringAsync()))
        {
            cufe = json.RootElement.GetProperty("cufe").GetString()!;
            invoiceId = json.RootElement.GetProperty("document").GetProperty("id").GetGuid();
        }

        var note = Edited(Path.Combine(GoldenIssueTests.GoldenRoot(), route, name), json => json["billing_reference"]!["uuid"] = cufe);
        using var early = await PostAsync(issuer.Client, route, note);
        await SetStatusAsync(invoiceId, DocumentStatus.Accepted);
        using var accepted = await PostAsync(issuer.Client, route, note);

        Assert.That(early.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), await early.Content.ReadAsStringAsync());
        using (var json = JsonDocument.Parse(await early.Content.ReadAsStringAsync()))
        {
            var root = json.RootElement;
            const string message =
                "La factura referenciada en billing_reference todavía no está aceptada por la DIAN. Emita la nota cuando la factura quede aceptada.";
            Assert.Multiple(() =>
            {
                Assert.That(root.GetProperty("code").GetString(), Is.EqualTo("document.reference_not_accepted"));
                Assert.That(root.GetProperty("success").GetBoolean(), Is.False);
                Assert.That(root.GetProperty("message").GetString(), Is.EqualTo(message));
                Assert.That(root.GetProperty("errors").GetProperty("billing_reference.uuid")[0].GetString(), Is.EqualTo(message));
            });
        }

        Assert.That(accepted.StatusCode, Is.EqualTo(HttpStatusCode.OK), await accepted.Content.ReadAsStringAsync());
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var notes = await CompanyApiTests.Rows<Document>(scope.Db, issuer.CompanyId).Where(row => row.DocumentTypeId != 1).ToListAsync();
        Assert.That(notes.Select(row => row.ReferenceDocumentId), Is.EqualTo(new Guid?[] { invoiceId }), "Only the second note was stored, linked.");
    }

    private static async Task<Issuer> NewIssuerAsync()
    {
        var (nit, dv) = CompaniesHost.NewNit();
        return await IssuerSetup.CreateAsync(nit, dv);
    }

    /// <summary>Un plan nuevo con la cantidad de facturas indicada, contratado por la empresa, con lo ya usado.</summary>
    private static async Task AssignPlanAsync(
        Issuer issuer, PlanPeriod period, long quantity, long used, DateTimeOffset? startsAt = null, bool planActive = true,
        bool assignmentActive = true)
    {
        var planId = Interlocked.Increment(ref planSequence);
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        scope.Db.Add(new Plan { Id = planId, Name = $"Plan {planId}", Period = period, IsActive = planActive });
        scope.Db.Add(new PlanQuota { PlanId = planId, DocumentKind = DocumentKind.Invoice, Quantity = quantity });
        var companyPlan = new CompanyPlan
        {
            CompanyId = issuer.CompanyId, PlanId = planId, StartsAt = startsAt ?? Api.Clock.UtcNow.AddDays(-1), IsActive = assignmentActive,
        };
        scope.Db.Add(companyPlan);
        await scope.Db.SaveChangesAsync();
        if (used > 0)
        {
            scope.Db.Add(new PlanUsage { CompanyPlanId = companyPlan.Id, DocumentKind = DocumentKind.Invoice, Used = used });
            await scope.Db.SaveChangesAsync();
        }
    }

    private static async Task SetStatusAsync(Guid documentId, DocumentStatus status)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        await scope.Db.Set<Document>().IgnoreQueryFilters().Where(row => row.Id == documentId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Status, status));
    }

    private static async Task<List<DocumentStatus>> DocumentsAsync(Issuer issuer)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        return await CompanyApiTests.Rows<Document>(scope.Db, issuer.CompanyId).Select(row => row.Status).ToListAsync();
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string code, string message)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), text);
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("success").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("message").GetString(), Is.EqualTo(message));
            Assert.That(root.GetProperty("code").GetString(), Is.EqualTo(code));
        });
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string route, byte[] body)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await client.PostAsync(IssueRoutes.Of(route), content);
    }

    private static byte[] Body(string caseDir) => File.ReadAllBytes(Path.Combine(caseDir, "input.json"));

    private static byte[] Edited(string caseDir, Action<JsonObject> change)
    {
        var node = JsonNode.Parse(Body(caseDir))!.AsObject();
        change(node);
        return Encoding.UTF8.GetBytes(node.ToJsonString());
    }
}
