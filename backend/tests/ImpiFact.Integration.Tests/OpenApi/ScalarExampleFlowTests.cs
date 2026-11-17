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
