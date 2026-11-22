using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ImpiFact.Application.Abstractions.Identity;
using AuthenticationSchemes = ImpiFact.Api.Identity.AuthenticationSchemes;

namespace ImpiFact.Integration.Tests.Identity;

/// <summary>
/// Contenido del documento OpenAPI (la descripción estándar de una API HTTP que muestra Scalar en <c>/scalar</c>):
/// solo documenta las rutas <c>/api/v1</c>, con una introducción, el esquema de autenticación Bearer y un resumen y una
/// descripción por operación y por sección. Las rutas heredadas del PHP se borraron en la fase 7a (T9b2).
/// </summary>
[TestFixture]
public sealed class OpenApiDocumentTests
{
    /// <summary>Rutas que solo existen en el host de pruebas (<see cref="TestEndpoints"/>), no en la API real.</summary>
    private const string TestOnlyPrefix = "/test/";

    private static readonly string[] HttpMethods = ["get", "put", "post", "delete", "patch", "head", "options", "trace"];

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
    public void Only_the_v1_routes_are_documented()
    {
        var paths = DocumentedPaths().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(paths, Is.Not.Empty);
            Assert.That(paths.Where(path => path.StartsWith("/api/ubl2.1", StringComparison.Ordinal)), Is.Empty);
            Assert.That(paths.Where(path => path.StartsWith("/api/download", StringComparison.Ordinal)), Is.Empty);
            Assert.That(paths.Where(path => !path.StartsWith("/api/v1/", StringComparison.Ordinal)), Is.Empty);
        });
    }

    [Test]
    public void Every_operation_has_a_summary_and_a_description()
    {
        var operations = Operations().ToList();

        Assert.That(operations, Is.Not.Empty);
        Assert.Multiple(() =>
        {
            foreach (var (name, operation) in operations)
            {
                Assert.That(Text(operation, "summary"), Is.Not.Empty, $"{name} has no summary.");
                Assert.That(Text(operation, "description"), Is.Not.Empty, $"{name} has no description.");
            }
        });
    }

    [Test]
    public void The_introduction_explains_the_flow_the_keys_the_scopes_and_the_errors()
    {
        var info = _document.GetProperty("info");
        var description = Text(info, "description");

        Assert.Multiple(() =>
        {
            Assert.That(Text(info, "title"), Is.EqualTo("ImpiFact API"));
            Assert.That(Text(info, "version"), Is.EqualTo("v1"));
            Assert.That(description, Does.Contain("Flujo"));
            Assert.That(description, Does.Contain("Worker"));
            Assert.That(description, Does.Contain("habilitación"));
            Assert.That(description, Does.Contain("producción"));
            Assert.That(description, Does.Contain("create-platform-key"));
            Assert.That(description, Does.Contain("apk_"));
            Assert.That(description, Does.Contain("/api/v1/auth/login"));
            Assert.That(description, Does.Contain("correlation_id"));
            Assert.That(description, Does.Contain("Idempotency-Key"));
            foreach (var scope in ApiScopes.All)
            {
                Assert.That(description, Does.Contain($"`{scope}`"), $"The introduction does not explain {scope}.");
            }
        });
    }

    [Test]
    public void A_bearer_security_scheme_is_declared()
    {
        var scheme = _document.GetProperty("components").GetProperty("securitySchemes").GetProperty(AuthenticationSchemes.Bearer);

        Assert.Multiple(() =>
        {
            Assert.That(Text(scheme, "type"), Is.EqualTo("http"));
            Assert.That(Text(scheme, "scheme"), Is.EqualTo("bearer"));
            Assert.That(Text(scheme, "description"), Is.Not.Empty);
        });
    }

    [Test]
    public void Secured_operations_require_the_bearer_scheme_and_anonymous_ones_do_not()
    {
        string[] anonymous =
        [
            "POST /api/v1/auth/login", "POST /api/v1/auth/refresh", "POST /api/v1/auth/logout",
            "POST /api/v1/auth/password-reset/request", "POST /api/v1/auth/password-reset/confirm",
        ];

        Assert.Multiple(() =>
        {
            foreach (var (name, operation) in Operations())
            {
                var schemes = operation.TryGetProperty("security", out var security)
                    ? security.EnumerateArray().SelectMany(requirement => requirement.EnumerateObject().Select(scheme => scheme.Name)).ToList()
                    : [];
                if (anonymous.Contains(name))
                {
                    Assert.That(schemes, Is.Empty, $"{name} is anonymous but asks for a credential.");
                }
                else
                {
                    Assert.That(schemes, Does.Contain(AuthenticationSchemes.Bearer), $"{name} does not ask for the bearer credential.");
                }
            }
        });
    }

    [TestCase("POST /api/v1/auth/login")]
    [TestCase("POST /api/v1/auth/refresh")]
    [TestCase("POST /api/v1/auth/logout")]
    public void The_session_operations_document_the_optional_panel_header_and_the_cookie(string name)
    {
        var operation = Operations().Single(candidate => candidate.Name == name).Operation;
        var header = operation.GetProperty("parameters").EnumerateArray()
            .Single(parameter => Text(parameter, "in") == "header" && Text(parameter, "name") == "X-Client");

        Assert.Multiple(() =>
        {
            Assert.That(header.TryGetProperty("required", out var required) && required.GetBoolean(), Is.False);
            Assert.That(header.GetProperty("example").GetString(), Is.EqualTo("panel"));
            Assert.That(Text(operation, "description"), Does.Contain("X-Client: panel").And.Contain("impifact_refresh"));
        });
    }

    [Test]
    public void Every_tag_in_use_has_a_description()
    {
        var used = Operations()
            .SelectMany(operation => operation.Operation.TryGetProperty("tags", out var tags) ? tags.EnumerateArray().Select(tag => tag.GetString()!) : [])
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var described = _document.TryGetProperty("tags", out var tags)
            ? tags.EnumerateArray().Where(tag => Text(tag, "description").Length > 0).Select(tag => Text(tag, "name")).ToHashSet(StringComparer.Ordinal)
            : [];

        Assert.That(used, Is.Not.Empty);
        Assert.That(used.Where(tag => !described.Contains(tag)), Is.Empty);
    }

    /// <summary>
    /// The control of <see cref="The_deleted_php_routes_answer_404"/>: with a key without scopes an existing route answers
    /// 403 (the route exists and the authorization refuses it), so a 404 there means the route is gone.
    /// </summary>
    [TestCase("POST", "/api/v1/documents/invoices")]
    [TestCase("GET", "/api/v1/information/900123456")]
    [TestCase("PUT", "/api/v1/companies/900123456/software")]
    [TestCase("GET", "/api/v1/plans")]
    public async Task An_existing_route_answers_403_to_a_key_without_scopes(string method, string path)
    {
        using var response = await SendWithoutScopesAsync(method, path);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>
    /// Phase 7a, T9b2 (S2, item 7): the routes of the PHP contract (<c>/api/ubl2.1/...</c>, <c>/api/information</c>,
    /// <c>/api/total_documents</c>, <c>/api/download</c>, <c>/api/load-document-by-cufe</c>, <c>/api/receivedfile</c>) were
    /// deleted; their behavior lives under <c>/api/v1</c>. At least one route of each endpoint module that had them.
    /// </summary>
    // CompanyEndpoints
    [TestCase("POST", "/api/ubl2.1/config/900123456/2")]
    [TestCase("PUT", "/api/ubl2.1/config/software")]
    [TestCase("PUT", "/api/ubl2.1/config/resolution")]
    [TestCase("GET", "/api/ubl2.1/certificates-listing/900123456")]
    [TestCase("POST", "/api/ubl2.1/numbering-range")]
    // DocumentIssueEndpoints
    [TestCase("POST", "/api/ubl2.1/invoice")]
    [TestCase("POST", "/api/ubl2.1/credit-note/0d2e2883-eb8d-4d5c-a4a6-1bd40f5a2b10")]
    [TestCase("POST", "/api/ubl2.1/support-document")]
    [TestCase("POST", "/api/ubl2.1/invoice-contingency-4/send_pendings")]
    // DocumentQueryEndpoints
    [TestCase("GET", "/api/information/900123456")]
    [TestCase("GET", "/api/total_documents/900123456")]
    [TestCase("GET", "/api/ubl2.1/invoice/current_number/1")]
    [TestCase("POST", "/api/ubl2.1/next-consecutive")]
    // DocumentUtilityEndpoints
    [TestCase("POST", "/api/ubl2.1/status/document/abc123")]
    [TestCase("POST", "/api/ubl2.1/xml/document/abc123")]
    [TestCase("GET", "/api/download/900123456/FES-SETP990000001.pdf")]
    [TestCase("GET", "/api/ubl2.1/download/900123456/FES-SETP990000001.pdf")]
    [TestCase("POST", "/api/ubl2.1/regeneratepdf")]
    [TestCase("POST", "/api/ubl2.1/send-email")]
    // DocumentBatchEndpoints
    [TestCase("POST", "/api/ubl2.1/add-to-batch/invoice/lote-1")]
    [TestCase("POST", "/api/ubl2.1/send-batch/lote-1")]
    // PlanEndpoints
    [TestCase("PUT", "/api/ubl2.1/plan")]
    [TestCase("GET", "/api/ubl2.1/plan/query")]
    [TestCase("GET", "/api/ubl2.1/plan/infoplanuser")]
    // RadianEndpoints
    [TestCase("POST", "/api/ubl2.1/send-event")]
    [TestCase("POST", "/api/ubl2.1/send-event-data")]
    [TestCase("POST", "/api/load-document-by-cufe")]
    [TestCase("POST", "/api/ubl2.1/status/events-document/abc123")]
    [TestCase("GET", "/api/receivedfile/900123456/abc123.xml")]
    public async Task The_deleted_php_routes_answer_404(string method, string path)
    {
        using var response = await SendWithoutScopesAsync(method, path);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private async Task<HttpResponseMessage> SendWithoutScopesAsync(string method, string path)
    {
        var (key, _) = _factory.AddApiKey(Guid.CreateVersion7(), []);
        using var client = _factory.CreateClient(key);
        return await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
    }

    /// <summary>
    /// La emisión lee el cuerpo a mano, sin mirar su tipo de contenido; describir ese cuerpo en el documento no debe
    /// agregar una restricción de tipo de contenido a la ruta (que respondería 415 antes de revisar la credencial).
    /// </summary>
    [TestCase("/api/v1/documents/invoices")]
    [TestCase("/api/v1/documents/credit-notes/test-set/test-set-id")]
    public async Task Describing_the_issue_body_does_not_restrict_its_content_type(string path)
    {
        var (key, _) = _factory.AddApiKey(Guid.CreateVersion7(), []);
        using var client = _factory.CreateClient(key);

        using var response = await client.PostAsync(path, new StringContent("{}", System.Text.Encoding.UTF8, "text/plain"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public void The_issue_operations_describe_their_json_body()
    {
        var operation = _document.GetProperty("paths").GetProperty("/api/v1/documents/invoices").GetProperty("post");

        Assert.That(
            operation.GetProperty("requestBody").GetProperty("content").TryGetProperty("application/json", out _),
            Is.True);
    }

    [Test]
    public async Task The_jwks_stays_published_outside_the_document()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/.well-known/jwks.json");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private IEnumerable<string> DocumentedPaths() =>
        _document.GetProperty("paths").EnumerateObject()
            .Select(path => path.Name)
            .Where(path => !path.StartsWith(TestOnlyPrefix, StringComparison.Ordinal));

    private IEnumerable<(string Name, JsonElement Operation)> Operations() =>
        _document.GetProperty("paths").EnumerateObject()
            .Where(path => !path.Name.StartsWith(TestOnlyPrefix, StringComparison.Ordinal))
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(item => HttpMethods.Contains(item.Name))
                .Select(item => ($"{item.Name.ToUpperInvariant()} {path.Name}", item.Value)));

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
}
