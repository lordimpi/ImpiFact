using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Integration.Tests.Protection;

// Vive en el espacio de nombres de las pruebas de emisión porque su SetUpFixture (DocumentIssueHost, el PostgreSQL
// desechable con la API real) solo cubre ese espacio de nombres.
namespace ImpiFact.Integration.Tests.DocumentIssue;

/// <summary>
/// Los ejemplos que Scalar (el visor de la API en <c>/scalar</c>) deja precargados funcionan tal cual: se toman del
/// documento OpenAPI de la API real, se configura la empresa con los ejemplos de la empresa, del software, del ambiente y
/// de las resoluciones (con los valores de ejemplo de <c>{nit}</c> y <c>{dv}</c>), y el ejemplo de factura emitido sin
/// cambios queda en cola (<c>Queued</c>). Solo el certificado no sale del ejemplo, porque el ejemplo no trae un archivo
/// real: se sube uno de prueba.
/// </summary>
[Category("Integration")]
public sealed class ScalarExampleFlowTests
{
    private JsonElement _document;

    [OneTimeSetUp]
    public async Task LoadDocumentAsync()
    {
        using var client = DocumentIssueHost.Api.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        _document = (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }

    [Test]
    public async Task The_preloaded_examples_configure_a_company_and_issue_the_invoice_as_is()
    {
        var nit = PathExample("/api/v1/companies/{nit}/{dv}", "post", "nit");
        var dv = PathExample("/api/v1/companies/{nit}/{dv}", "post", "dv");
        using var platform = await DocumentIssueHost.Api.ClientWithNewKeyAsync(null, ApiScopes.Platform);

        string token;
        using (var created = await SendAsync(platform, HttpMethod.Post, $"/api/v1/companies/{nit}/{dv}", Example("/api/v1/companies/{nit}/{dv}", "post")))
        {
            var text = await created.Content.ReadAsStringAsync();
            Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
            using var json = JsonDocument.Parse(text);
            token = json.RootElement.GetProperty("token").GetString()!;
        }

        using var company = DocumentIssueHost.Api.ClientWith(token);
        await OkAsync(company, HttpMethod.Put, $"/api/v1/companies/{nit}/software", Example("/api/v1/companies/{nit}/software", "put"));
        await OkAsync(company, HttpMethod.Put, $"/api/v1/companies/{nit}/environment", Example("/api/v1/companies/{nit}/environment", "put", "habilitacion"));
        await OkAsync(company, HttpMethod.Put, $"/api/v1/companies/{nit}/certificate",
            JsonSerializer.Serialize(new { certificate = Convert.ToBase64String(TestPfx.Rsa()), password = TestPfx.Password }));
        foreach (var resolution in Examples("/api/v1/companies/{nit}/resolution", "put"))
        {
            await OkAsync(company, HttpMethod.Put, $"/api/v1/companies/{nit}/resolution", resolution);
        }

        using var issued = await SendAsync(company, HttpMethod.Post, "/api/v1/documents/invoices",
            Example("/api/v1/documents/invoices", "post", "single-line-iva19"));

        var body = await issued.Content.ReadAsStringAsync();
        Assert.That(issued.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
        using var issuedJson = JsonDocument.Parse(body);
        var document = issuedJson.RootElement.GetProperty("document");
        Assert.Multiple(() =>
        {
            Assert.That(document.GetProperty("status").GetString(), Is.EqualTo("Queued"));
            Assert.That(document.GetProperty("full_number").GetString(), Is.EqualTo("SETP990000002"));
        });

        // The invoice variant examples (phase 7a, T2) are issued as they are too, after the standard invoice.
        foreach (var path in new[] { "/api/v1/documents/invoices/aiu", "/api/v1/documents/invoices/mandate", "/api/v1/documents/invoices/transport" })
        {
            using var variant = await SendAsync(company, HttpMethod.Post, path, Example(path, "post"));
            var text = await variant.Content.ReadAsStringAsync();
            Assert.That(variant.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"{path}: {text}");
            using var json = JsonDocument.Parse(text);
            Assert.That(json.RootElement.GetProperty("document").GetProperty("status").GetString(), Is.EqualTo("Queued"), path);
        }

        // The export examples (phase 7a, T3) use the type 2 resolution example and its own sequence.
        var exportNumbers = new List<string>();
        foreach (var name in new[] { "export-cop-cif", "export-usd-fob" })
        {
            using var export = await SendAsync(company, HttpMethod.Post, "/api/v1/documents/invoices/export",
                Example("/api/v1/documents/invoices/export", "post", name));
            var text = await export.Content.ReadAsStringAsync();
            Assert.That(export.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"{name}: {text}");
            using var json = JsonDocument.Parse(text);
            var exported = json.RootElement.GetProperty("document");
            Assert.That((exported.GetProperty("status").GetString(), exported.GetProperty("document_type_id").GetInt32()), Is.EqualTo(("Queued", 2)), name);
            exportNumbers.Add(exported.GetProperty("full_number").GetString()!);
        }

        Assert.That(exportNumbers, Is.EqualTo(new[] { "SETP990000000", "SETP990000001" }));

        // The support document example (phase 7a, T4) needs the support-document software example and the SEDS resolution.
        await OkAsync(company, HttpMethod.Put, $"/api/v1/companies/{nit}/software", Example("/api/v1/companies/{nit}/software", "put", "documento-soporte"));
        using var supportDocument = await SendAsync(company, HttpMethod.Post, "/api/v1/documents/support-documents",
            Example("/api/v1/documents/support-documents", "post", "support-document-resident"));
        var supportText = await supportDocument.Content.ReadAsStringAsync();
        Assert.That(supportDocument.StatusCode, Is.EqualTo(HttpStatusCode.OK), supportText);
        using var supportJson = JsonDocument.Parse(supportText);
        var issuedSupport = supportJson.RootElement.GetProperty("document");
        Assert.That((issuedSupport.GetProperty("status").GetString(), issuedSupport.GetProperty("full_number").GetString()),
            Is.EqualTo(("Queued", "SEDS984000000")));

        // The adjustment note example references a support document the integrator must replace: as is, it answers 422.
        using var note = await SendAsync(company, HttpMethod.Post, "/api/v1/documents/support-documents/adjustment-notes",
            Example("/api/v1/documents/support-documents/adjustment-notes", "post"));
        var noteText = await note.Content.ReadAsStringAsync();
        Assert.That(note.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), noteText);
        Assert.That(noteText, Does.Contain("document.reference_not_found"));
    }

    private string PathExample(string path, string method, string name) =>
        Operation(path, method).GetProperty("parameters").EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == name)
            .GetProperty("example").GetString()!;

    private string Example(string path, string method, string? name = null)
    {
        var examples = Operation(path, method).GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("examples");
        var example = name is null ? examples.EnumerateObject().First().Value : examples.GetProperty(name);
        return example.GetProperty("value").GetRawText();
    }

    private IEnumerable<string> Examples(string path, string method) =>
        Operation(path, method).GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("examples")
            .EnumerateObject().Select(example => example.Value.GetProperty("value").GetRawText()).ToList();

    private JsonElement Operation(string path, string method) => _document.GetProperty("paths").GetProperty(path).GetProperty(method);

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string json)
    {
        var content = new StringContent(json, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await client.SendAsync(new HttpRequestMessage(method, path) { Content = content });
    }

    private static async Task OkAsync(HttpClient client, HttpMethod method, string path, string json)
    {
        using var response = await SendAsync(client, method, path, json);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"{method} {path}: {await response.Content.ReadAsStringAsync()}");
    }
}
