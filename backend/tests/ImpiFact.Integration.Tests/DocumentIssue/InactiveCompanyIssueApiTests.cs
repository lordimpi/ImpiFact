using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Documents;
using ImpiFact.Integration.Tests.Companies;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Integration.Tests.DocumentIssue;

/// <summary>
/// A company the platform deactivated issues nothing (phase 7a, T9b1; decision L17): every family answers 403
/// <c>company.inactive</c> before numbering or calling the DIAN, even with a credential that is still accepted (another
/// instance may keep a revoked key in its cache for up to 60 seconds, and a panel access token lives up to 15 minutes).
/// Its own API key stops working at once, and the documents it already issued are kept.
/// </summary>
[Category("Integration")]
public sealed class InactiveCompanyIssueApiTests
{
    private const string InactiveMessage = "La empresa se encuentra en el momento INACTIVA para enviar documentos electronicos...";

    private static CompanyApiFactory Api => DocumentIssueHost.Api;

    [Test]
    public async Task Every_family_is_refused_before_numbering_or_calling_the_dian_and_stored_documents_are_kept()
    {
        var (nit, dv) = CompaniesHost.NewNit();
        using var issuer = await IssuerSetup.CreateAsync(nit, dv);
        using var issued = await PostAsync(issuer.Client, "/api/v1/documents/invoices", Golden("invoice", "single-line-iva19"));
        Assert.That(issued.StatusCode, Is.EqualTo(HttpStatusCode.OK), await issued.Content.ReadAsStringAsync());
        Guid documentId;
        using (var json = JsonDocument.Parse(await issued.Content.ReadAsStringAsync()))
        {
            documentId = json.RootElement.GetProperty("document").GetProperty("id").GetGuid();
        }

        Guid batchId;
        using (var batch = await issuer.Client.PostAsJsonAsync("/api/v1/documents/batches", new { name = $"lote-{nit}" }))
        {
            var text = await batch.Content.ReadAsStringAsync();
            Assert.That(batch.StatusCode, Is.EqualTo(HttpStatusCode.Created).Or.EqualTo(HttpStatusCode.OK), text);
            using var json = JsonDocument.Parse(text);
            batchId = json.RootElement.GetProperty("id").GetGuid();
        }

        using var platform = await Api.ClientWithNewKeyAsync(null, ApiScopes.Platform);
        using (var deactivated = await platform.PostAsync($"/api/v1/platform/companies/{nit}/deactivate", null))
        {
            Assert.That(deactivated.StatusCode, Is.EqualTo(HttpStatusCode.OK), await deactivated.Content.ReadAsStringAsync());
        }

        var documentsBefore = await DocumentCountAsync(issuer.CompanyId);
        var dianCallsBefore = Api.Dian.Requests.Count;
        using var ownKey = await PostAsync(issuer.Client, "/api/v1/documents/invoices", Golden("invoice", "two-lines-discounts-iva19"));
        using var survivor = await Api.ClientWithNewKeyAsync(issuer.CompanyId, ApiScopes.DocumentsWrite, ApiScopes.DocumentsRead, ApiScopes.ConfigWrite);

        var attempts = new (string Name, Func<Task<HttpResponseMessage>> Send)[]
        {
            ("invoice", () => PostAsync(survivor, "/api/v1/documents/invoices", Golden("invoice", "two-lines-discounts-iva19"))),
            ("contingency 03", () => PostAsync(survivor, "/api/v1/documents/invoices/contingency", Golden("invoice-contingency", "paper-backup-reference"))),
            ("contingency 04", () => PostAsync(survivor, "/api/v1/documents/invoices/dian-contingency", Golden("invoice-contingency-4", "pending-transmission"))),
            ("send pending 04", () => survivor.PostAsJsonAsync("/api/v1/documents/invoices/dian-contingency/send-pending", new { })),
            ("credit note", () => PostAsync(survivor, "/api/v1/documents/credit-notes", Golden("credit-note", "without-reference-invoice-period"))),
            ("debit note", () => PostAsync(survivor, "/api/v1/documents/debit-notes", Golden("debit-note", "price-adjustment-with-reference"))),
            ("support document", () => PostAsync(survivor, "/api/v1/documents/support-documents", Golden("support-document", "resident-seller"))),
            ("adjustment note", () => PostAsync(survivor, "/api/v1/documents/support-documents/adjustment-notes", Golden("sd-credit-note", "adjustment-with-reference"))),
            ("preview", () => PostAsync(survivor, "/api/v1/documents/invoices/preview", Golden("invoice", "single-line-iva19"))),
            ("batch add", () => PostAsync(survivor, $"/api/v1/documents/batches/{batchId}/documents/invoice", Golden("invoice", "final-consumer"))),
            ("batch send", () => survivor.PostAsync($"/api/v1/documents/batches/{batchId}/send", null)),
            ("radian load", () => survivor.PostAsJsonAsync("/api/v1/received-documents", new { cufe = new string('a', 96) })),
        };

        var answers = new List<(string Name, HttpStatusCode Status, string? Code, string? Detail)>();
        foreach (var (name, send) in attempts)
        {
            using var response = await send();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync() is { Length: > 0 } text ? text : "{}");
            answers.Add((name, response.StatusCode, Read(json, "code"), Read(json, "detail")));
        }

        using var stored = await survivor.GetAsync($"/api/v1/documents/{documentId}");

        Assert.Multiple(async () =>
        {
            Assert.That(ownKey.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "Deactivating revoked the company API key.");
            foreach (var answer in answers)
            {
                Assert.That((answer.Status, answer.Code, answer.Detail), Is.EqualTo((HttpStatusCode.Forbidden, "company.inactive", InactiveMessage)), answer.Name);
            }

            Assert.That(await DocumentCountAsync(issuer.CompanyId), Is.EqualTo(documentsBefore), "Nothing was numbered or stored.");
            Assert.That(Api.Dian.Requests.Count, Is.EqualTo(dianCallsBefore), "The DIAN was never called.");
            Assert.That(stored.StatusCode, Is.EqualTo(HttpStatusCode.OK), "The documents already issued are kept.");
        });
    }

    private static byte[] Golden(string family, string name) =>
        File.ReadAllBytes(Path.Combine(GoldenIssueTests.GoldenRoot(), family, name, "input.json"));

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string route, byte[] body)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await client.PostAsync(route, content);
    }

    private static string? Read(JsonDocument json, string property) =>
        json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty(property, out var value) ? value.GetString() : null;

    private static async Task<int> DocumentCountAsync(Guid companyId)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        return await CompanyApiTests.Rows<Document>(scope.Db, companyId).CountAsync();
    }
}
