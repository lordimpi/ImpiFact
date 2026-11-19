using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Platform;
using ImpiFact.Integration.Tests.Companies;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Integration.Tests.DocumentIssue;

/// <summary>
/// Vista previa del PDF de una nota crédito o débito antes de emitirla (<c>POST /api/v1/documents/credit-notes/preview</c>
/// y <c>/debit-notes/preview</c>): el mismo camino que la vista previa de la factura, con el cuerpo de
/// <c>issueCreditNote</c> o <c>issueDebitNote</c>, las plantillas reales y un Gotenberg falso, y nada guardado.
/// </summary>
[Category("Integration")]
public sealed class NotePreviewApiTests
{
    private static CompanyApiFactory Api => DocumentIssueHost.Api;

    [TearDown]
    public void GotenbergUp() => Api.Gotenberg.Down = false;

    [TestCase("credit-notes", "credit-note", "NC - BORRADOR", "NOTA CREDITO ELECTRONICA")]
    [TestCase("debit-notes", "debit-note", "ND - BORRADOR", "NOTA DEBITO ELECTRONICA")]
    public async Task The_preview_answers_the_pdf_of_the_note_marked_as_a_draft(string route, string goldenType, string number, string title)
    {
        using var issuer = await NewIssuerAsync();

        using var response = await PostAsync(issuer.Client, route, Golden(goldenType, json => json.Remove("number")));

        var conversion = Api.Gotenberg.ConversionsWith(issuer.Nit).Single();
        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/pdf"));
            Assert.That(response.Content.Headers.ContentDisposition?.DispositionType, Is.EqualTo("inline"));
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(await response.Content.ReadAsByteArrayAsync(), Is.EqualTo(FakeGotenberg.Pdf));
            Assert.That(conversion.Html, Does.Contain("VISTA PREVIA - SIN VALIDEZ"));
            Assert.That(conversion.Html, Does.Contain(number));
            Assert.That(conversion.Html, Does.Contain(title));
            Assert.That(conversion.Html, Does.Contain("CUDE: <span class=\"uuid\">VISTA PREVIA - SIN CUDE</span>"));
        });
    }

    [TestCase("credit-notes", "credit-note")]
    [TestCase("debit-notes", "debit-note")]
    public async Task The_preview_saves_nothing_and_does_not_take_a_number(string route, string goldenType)
    {
        using var issuer = await NewIssuerAsync();

        using var preview = await PostAsync(issuer.Client, route, Golden(goldenType, json => json.Remove("number")));

        await using var scope = Api.OpenScope(null, null, ActorType.System);
        Assert.Multiple(async () =>
        {
            Assert.That(preview.StatusCode, Is.EqualTo(HttpStatusCode.OK), await preview.Content.ReadAsStringAsync());
            Assert.That(await CompanyApiTests.Rows<Document>(scope.Db, issuer.CompanyId).CountAsync(), Is.Zero);
            Assert.That(await CompanyApiTests.Rows<DocumentFile>(scope.Db, issuer.CompanyId).CountAsync(), Is.Zero);
            Assert.That(await scope.Db.Set<OutboxMessage>().CountAsync(row => row.CompanyId == issuer.CompanyId), Is.Zero);
        });
    }

    // Las mismas validaciones de forma y de totales de la emisión de cada nota, con los mismos campos.
    [Test]
    public async Task Invalid_data_answers_422_like_the_issue_and_nothing_is_rendered()
    {
        using var issuer = await NewIssuerAsync();

        using var credit = await PostAsync(issuer.Client, "credit-notes", Golden("credit-note", json => json.Remove("credit_note_lines")));
        using var debit = await PostAsync(issuer.Client, "debit-notes",
            Golden("debit-note", json => json["requested_monetary_totals"]!["payable_amount"] = "1.00"));
        using var invoiceBody = await PostAsync(issuer.Client, "credit-notes", Golden("invoice", json => json.Remove("number")));

        Assert.Multiple(async () =>
        {
            Assert.That(credit.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), await credit.Content.ReadAsStringAsync());
            Assert.That(await Fields(credit), Does.Contain("credit_note_lines"));
            Assert.That(debit.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), await debit.Content.ReadAsStringAsync());
            Assert.That(await Fields(debit), Does.Contain("requested_monetary_totals.payable_amount"));
            Assert.That(invoiceBody.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(Api.Gotenberg.ConversionsWith(issuer.Nit), Is.Empty);
        });
    }

    [TestCase("credit-notes")]
    [TestCase("debit-notes")]
    public async Task A_read_only_credential_cannot_preview_a_note(string route)
    {
        using var issuer = await NewIssuerAsync();
        using var reader = await Api.ClientWithNewKeyAsync(issuer.CompanyId, ApiScopes.DocumentsRead);

        using var response = await PostAsync(reader, route, Golden(route == "credit-notes" ? "credit-note" : "debit-note", _ => { }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    private static async Task<Issuer> NewIssuerAsync()
    {
        var (nit, dv) = CompaniesHost.NewNit();
        return await IssuerSetup.CreateAsync(nit, dv);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string route, byte[] body)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await client.PostAsync($"/api/v1/documents/{route}/preview", content);
    }

    private static byte[] Golden(string type, Action<JsonObject> change)
    {
        var caseDir = Directory.GetDirectories(Path.Combine(GoldenIssueTests.GoldenRoot(), type)).Order(StringComparer.Ordinal).First();
        var node = JsonNode.Parse(File.ReadAllBytes(Path.Combine(caseDir, "input.json")))!.AsObject();
        change(node);
        return Encoding.UTF8.GetBytes(node.ToJsonString());
    }

    private static async Task<List<string>> Fields(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name).ToList();
    }
}
