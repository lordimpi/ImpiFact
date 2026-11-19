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
/// Vista previa del PDF de una factura antes de emitirla (<c>POST /api/v1/documents/invoices/preview</c>, el
/// <c>invoice/preeliminar-view</c> del PHP): mismas validaciones que la emisión, el PDF con las plantillas reales y un
/// Gotenberg falso, y nada guardado: ni documento, ni consecutivo, ni archivos, ni mensajes de la bandeja de salida.
/// </summary>
[Category("Integration")]
public sealed class InvoicePreviewApiTests
{
    private const string Route = "/api/v1/documents/invoices/preview";

    private static CompanyApiFactory Api => DocumentIssueHost.Api;

    private static string Invoice => Path.Combine(GoldenIssueTests.GoldenRoot(), "invoice", "single-line-iva19");

    [TearDown]
    public void GotenbergUp() => Api.Gotenberg.Down = false;

    [Test]
    public async Task The_preview_answers_the_pdf_of_the_invoice_marked_as_a_draft()
    {
        using var issuer = await NewIssuerAsync();

        using var response = await PostAsync(issuer.Client, Edited(json => json.Remove("number")));

        var conversion = Api.Gotenberg.ConversionsWith(issuer.Nit).Single();
        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/pdf"));
            Assert.That(response.Content.Headers.ContentDisposition?.DispositionType, Is.EqualTo("inline"));
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(await response.Content.ReadAsByteArrayAsync(), Is.EqualTo(FakeGotenberg.Pdf));
            Assert.That(conversion.Html, Does.Contain("VISTA PREVIA - SIN VALIDEZ"));
            Assert.That(conversion.Html, Does.Contain("SETP - BORRADOR"));
            Assert.That(conversion.Html, Does.Contain("CUFE: <span class=\"uuid\">VISTA PREVIA - SIN CUFE</span>"));
            Assert.That(conversion.Html, Does.Contain("EMPRESA DE PRUEBA GOLDEN SAS"));
            Assert.That(conversion.Html, Does.Contain("Resolución de Facturación Electrónica No. 18760000001"));
            Assert.That(conversion.Files.Keys, Is.EquivalentTo(new[] { "index.html", "styles.css", "qr.png" }));
        });
    }

    [Test]
    public async Task The_preview_saves_nothing_and_does_not_take_a_number()
    {
        using var issuer = await NewIssuerAsync();
        var body = Edited(json => json.Remove("number"));

        using var preview = await PostAsync(issuer.Client, body);
        using var again = await PostAsync(issuer.Client, body);

        await using (var scope = Api.OpenScope(null, null, ActorType.System))
        {
            Assert.Multiple(async () =>
            {
                Assert.That(preview.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(await CompanyApiTests.Rows<Document>(scope.Db, issuer.CompanyId).CountAsync(), Is.Zero);
                Assert.That(await CompanyApiTests.Rows<DocumentFile>(scope.Db, issuer.CompanyId).CountAsync(), Is.Zero);
                Assert.That(await CompanyApiTests.Rows<IdempotencyKey>(scope.Db, issuer.CompanyId).CountAsync(), Is.Zero);
                Assert.That(await scope.Db.Set<OutboxMessage>().CountAsync(row => row.CompanyId == issuer.CompanyId), Is.Zero);
            });
        }

        using var issued = await issuer.Client.PostAsync("/api/v1/documents/invoices", Json(body));
        using var json = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
        Assert.That(json.RootElement.GetProperty("document").GetProperty("number").GetInt64(), Is.EqualTo(990000000), "The first real invoice still gets the first number.");
    }

    [Test]
    public async Task A_company_without_certificate_or_software_can_preview()
    {
        using var issuer = await NewIssuerAsync(withCertificate: false, withSoftware: false);

        using var response = await PostAsync(issuer.Client, Edited(json => json.Remove("number")));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task The_company_logo_goes_to_gotenberg_with_the_preview()
    {
        using var issuer = await NewIssuerAsync();
        using (var saved = await issuer.Client.PutAsync(
            $"/api/v1/companies/{issuer.Nit}/logo", CompanyLogoApiTests.Form(CompanyLogoApiTests.Png, "logo.png", "image/png")))
        {
            Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.OK), await saved.Content.ReadAsStringAsync());
        }

        using var response = await PostAsync(issuer.Client, Edited(json => json.Remove("number")));

        var conversion = Api.Gotenberg.ConversionsWith(issuer.Nit).Single();
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(conversion.Files["logo.png"].Content, Is.EqualTo(CompanyLogoApiTests.Png));
            Assert.That(conversion.Files["logo.png"].ContentType, Is.EqualTo("image/png"));
            Assert.That(conversion.Html, Does.Contain("src=\"logo.png\""));
        });
    }

    [Test]
    public async Task Invalid_data_answers_422_like_the_issue_and_nothing_is_rendered()
    {
        using var issuer = await NewIssuerAsync();

        using var shape = await PostAsync(issuer.Client, Edited(json => json.Remove("customer")));
        using var totals = await PostAsync(issuer.Client, Edited(json => json["legal_monetary_totals"]!["payable_amount"] = "1.00"));
        using var notJson = await PostAsync(issuer.Client, "{no es json"u8.ToArray());

        Assert.Multiple(async () =>
        {
            Assert.That(shape.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), await shape.Content.ReadAsStringAsync());
            Assert.That(totals.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), await totals.Content.ReadAsStringAsync());
            Assert.That(await Code(totals), Is.EqualTo("validation.failed"));
            Assert.That(notJson.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(Api.Gotenberg.ConversionsWith(issuer.Nit), Is.Empty);
        });
    }

    [Test]
    public async Task Without_a_resolution_the_preview_answers_422()
    {
        using var issuer = await NewIssuerAsync(resolutions: []);

        using var response = await PostAsync(issuer.Client, Edited(json => json.Remove("number")));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task Gotenberg_down_answers_503_with_retry_after()
    {
        using var issuer = await NewIssuerAsync();
        Api.Gotenberg.Down = true;

        using var response = await PostAsync(issuer.Client, Edited(json => json.Remove("number")));

        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(await Code(response), Is.EqualTo("pdf.unavailable"));
            Assert.That(response.Headers.RetryAfter, Is.Not.Null);
        });
    }

    [Test]
    public async Task A_read_only_credential_cannot_preview()
    {
        using var issuer = await NewIssuerAsync();
        using var reader = await Api.ClientWithNewKeyAsync(issuer.CompanyId, ApiScopes.DocumentsRead);

        using var response = await PostAsync(reader, Edited(json => json.Remove("number")));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    private static async Task<Issuer> NewIssuerAsync(bool withCertificate = true, bool withSoftware = true, IEnumerable<object>? resolutions = null)
    {
        var (nit, dv) = CompaniesHost.NewNit();
        return await IssuerSetup.CreateAsync(nit, dv, withCertificate, withSoftware, resolutions);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, byte[] body) => await client.PostAsync(Route, Json(body));

    private static ByteArrayContent Json(byte[] body)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    private static byte[] Edited(Action<JsonObject> change)
    {
        var node = JsonNode.Parse(File.ReadAllBytes(Path.Combine(Invoice, "input.json")))!.AsObject();
        change(node);
        return Encoding.UTF8.GetBytes(node.ToJsonString());
    }

    private static async Task<string?> Code(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("code").GetString();
    }
}
