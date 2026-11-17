using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImpiFact.Integration.Tests.DocumentIssue;

namespace ImpiFact.Integration.Tests.Identity;

/// <summary>
/// Ejemplos listos para enviar en el documento OpenAPI (la descripción estándar de la API que muestra Scalar en
/// <c>/scalar</c>): cada cuerpo JSON documentado trae al menos un ejemplo con su resumen, los de emisión son copias de
/// los casos golden (<c>tests/golden/.../input.json</c>, solicitudes que la DIAN aceptó), los parámetros de la ruta
/// traen un valor de ejemplo y la introducción empieza con la guía rápida.
/// </summary>
[TestFixture]
public sealed class OpenApiExampleTests
{
    private const string TestOnlyPrefix = "/test/";

    private static readonly string[] HttpMethods = ["get", "put", "post", "delete", "patch"];

    private IdentityApiFactory _factory = null!;
    private JsonElement _document;

    [OneTimeSetUp]
    public async Task LoadDocument()
    {
        _factory = new IdentityApiFactory();
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        _document = (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }

    [OneTimeTearDown]
    public void StopApi() => _factory.Dispose();

    [Test]
    public void Every_json_request_body_offers_at_least_one_example_with_a_summary()
    {
        var bodies = Operations()
            .Where(operation => JsonContent(operation.Operation) is not null)
            .ToList();

        Assert.That(bodies, Has.Count.GreaterThanOrEqualTo(18), "Expected every documented JSON body to be found.");
        Assert.Multiple(() =>
        {
            foreach (var (name, operation) in bodies)
            {
                var examples = Examples(operation);
                Assert.That(examples, Is.Not.Empty, $"{name} has no request example.");
                foreach (var (key, example) in examples)
                {
                    Assert.That(Text(example, "summary"), Is.Not.Empty, $"{name} example {key} has no summary.");
                    Assert.That(example.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Object, Is.True,
                        $"{name} example {key} has no JSON object value.");
                }
            }
        });
    }

    [TestCase("/api/v1/documents/invoices", "invoice",
        "single-line-iva19", "two-lines-discounts-iva19", "mixed-iva-19-5-0", "final-consumer")]
    [TestCase("/api/v1/documents/invoices/test-set/{testSetId}", "invoice",
        "single-line-iva19", "two-lines-discounts-iva19", "mixed-iva-19-5-0", "final-consumer")]
    [TestCase("/api/v1/documents/credit-notes", "credit-note", "partial-return-with-reference", "without-reference-invoice-period")]
    [TestCase("/api/v1/documents/credit-notes/test-set/{testSetId}", "credit-note",
        "partial-return-with-reference", "without-reference-invoice-period")]
    [TestCase("/api/v1/documents/debit-notes", "debit-note", "price-adjustment-with-reference")]
    [TestCase("/api/v1/documents/debit-notes/test-set/{testSetId}", "debit-note", "price-adjustment-with-reference")]
    public void Issue_examples_are_the_golden_inputs(string path, string family, params string[] cases)
    {
        var examples = Examples(_document.GetProperty("paths").GetProperty(path).GetProperty("post"));

        Assert.That(examples.Select(example => example.Name), Is.EquivalentTo(cases));
        Assert.Multiple(() =>
        {
            foreach (var goldenCase in cases)
            {
                var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(GoldenIssueTests.GoldenRoot(), family, goldenCase, "input.json")));
                var actual = JsonNode.Parse(examples.Single(example => example.Name == goldenCase).Example.GetProperty("value").GetRawText());
                Assert.That(JsonNode.DeepEquals(actual, expected), Is.True, $"{path} example {goldenCase} differs from its golden input.");
            }
        });
    }

    [Test]
    public void Invoice_examples_use_the_habilitacion_test_resolution()
    {
        var examples = Examples(_document.GetProperty("paths").GetProperty("/api/v1/documents/invoices").GetProperty("post"));

        Assert.That(examples, Is.Not.Empty);
        Assert.Multiple(() =>
        {
            foreach (var (name, example) in examples)
            {
                var value = example.GetProperty("value");
                Assert.That(Text(value, "prefix"), Is.EqualTo("SETP"), name);
                Assert.That(Text(value, "resolution_number"), Is.EqualTo("18760000001"), name);
                Assert.That(value.GetProperty("number").GetInt64(), Is.InRange(990000000, 995000000), name);
            }
        });
    }

    [Test]
    public void The_resolution_example_is_the_habilitacion_test_range()
    {
        var examples = Examples(_document.GetProperty("paths").GetProperty("/api/v1/companies/{nit}/resolution").GetProperty("put"));
        var invoice = examples.Select(example => example.Example.GetProperty("value"))
            .Single(value => value.GetProperty("type_document_id").GetInt32() == 1);

        Assert.Multiple(() =>
        {
            Assert.That(Text(invoice, "prefix"), Is.EqualTo("SETP"));
            Assert.That(Text(invoice, "resolution"), Is.EqualTo("18760000001"));
            Assert.That(invoice.GetProperty("from").GetInt64(), Is.EqualTo(990000000));
            Assert.That(invoice.GetProperty("to").GetInt64(), Is.EqualTo(995000000));
        });
    }

    [Test]
    public void Every_path_parameter_has_an_example_value()
    {
        var parameters = Operations()
            .SelectMany(operation => operation.Operation.TryGetProperty("parameters", out var list)
                ? list.EnumerateArray().Where(parameter => Text(parameter, "in") == "path").Select(parameter => (operation.Name, Parameter: parameter))
                : [])
            .ToList();

        Assert.That(parameters, Is.Not.Empty);
        Assert.Multiple(() =>
        {
            foreach (var (name, parameter) in parameters)
            {
                Assert.That(parameter.TryGetProperty("example", out _), Is.True, $"{name} parameter {Text(parameter, "name")} has no example.");
            }
        });
    }

    [Test]
    public void The_company_path_examples_are_a_valid_nit_and_check_digit()
    {
        var parameters = _document.GetProperty("paths").GetProperty("/api/v1/companies/{nit}/{dv}").GetProperty("post")
            .GetProperty("parameters").EnumerateArray().ToDictionary(parameter => Text(parameter, "name"), parameter => parameter.GetProperty("example").GetString());

        Assert.That(parameters, Is.EquivalentTo(new Dictionary<string, string?> { ["nit"] = "900123456", ["dv"] = "8" }));
    }

    [Test]
    public void The_introduction_starts_with_the_quick_guide()
    {
        var description = Text(_document.GetProperty("info"), "description");
        var guide = description.IndexOf("Pruebe en este orden", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(guide, Is.GreaterThanOrEqualTo(0), "The introduction has no quick guide.");
            Assert.That(guide, Is.LessThan(description.IndexOf("## Flujo de integración", StringComparison.Ordinal)), "The quick guide is not first.");
            Assert.That(description, Does.Contain("create-platform-key"));
            Assert.That(description, Does.Contain("seed-demo"));
            Assert.That(description, Does.Contain("POST /api/v1/companies/{nit}/{dv}"));
            Assert.That(description, Does.Contain("PUT /api/v1/companies/{nit}/software"));
            Assert.That(description, Does.Contain("POST /api/v1/documents/invoices"));
            Assert.That(description, Does.Contain("GET /api/v1/documents/{id}"));
        });
    }

    private IEnumerable<(string Name, JsonElement Operation)> Operations() =>
        _document.GetProperty("paths").EnumerateObject()
            .Where(path => !path.Name.StartsWith(TestOnlyPrefix, StringComparison.Ordinal))
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(item => HttpMethods.Contains(item.Name))
                .Select(item => ($"{item.Name.ToUpperInvariant()} {path.Name}", item.Value)));

    private static JsonElement? JsonContent(JsonElement operation) =>
        operation.TryGetProperty("requestBody", out var body)
        && body.TryGetProperty("content", out var content)
        && content.TryGetProperty("application/json", out var json)
            ? json
            : null;

    private static List<(string Name, JsonElement Example)> Examples(JsonElement operation) =>
        JsonContent(operation) is { } json && json.TryGetProperty("examples", out var examples)
            ? examples.EnumerateObject().Select(example => (example.Name, example.Value)).ToList()
            : [];

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
}
