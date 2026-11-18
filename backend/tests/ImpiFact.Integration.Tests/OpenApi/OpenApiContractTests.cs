using System.Net.Http.Json;
using System.Text.Json;
using ImpiFact.Integration.Tests.Identity;

namespace ImpiFact.Integration.Tests.OpenApi;

/// <summary>
/// El documento OpenAPI (la descripción estándar de una API HTTP) es el contrato del que se genera el cliente del panel
/// (<c>web/projects/api</c>, con <c>npm run api:generate</c>). Por eso cada operación tiene un <c>operationId</c>
/// legible y estable (el nombre del método en el cliente generado), la respuesta del panel en la sesión está descrita
/// y el documento sale igual en cada generación, para que la verificación de diferencias del CI (integración continua)
/// tenga sentido.
/// </summary>
[TestFixture]
public sealed class OpenApiContractTests
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
        _document = await DocumentOf(_factory);
    }

    [OneTimeTearDown]
    public void StopApi() => _factory.Dispose();

    [Test]
    public void Every_operation_has_a_unique_camel_case_operation_id()
    {
        var operations = Operations().ToList();
        var ids = operations.Select(operation => Text(operation.Operation, "operationId")).ToList();

        Assert.That(operations, Is.Not.Empty);
        Assert.Multiple(() =>
        {
            foreach (var (name, operation) in operations)
            {
                Assert.That(Text(operation, "operationId"), Does.Match("^[a-z][a-zA-Z0-9]*$"), $"{name} has no camelCase operationId.");
            }

            Assert.That(ids.GroupBy(id => id).Where(group => group.Count() > 1).Select(group => group.Key), Is.Empty, "Repeated operationIds.");
        });
    }

    /// <summary>
    /// Cada <c>operationId</c> es el nombre de un método del cliente del panel: cambiarlo rompe el código que lo usa. Esta
    /// lista obliga a que un cambio de nombre sea deliberado.
    /// </summary>
    [TestCase("POST /api/v1/auth/login", "login")]
    [TestCase("POST /api/v1/auth/refresh", "refreshSession")]
    [TestCase("POST /api/v1/auth/logout", "logout")]
    [TestCase("POST /api/v1/api-keys", "createApiKey")]
    [TestCase("DELETE /api/v1/api-keys/{id}", "revokeApiKey")]
    [TestCase("POST /api/v1/users", "createUser")]
    [TestCase("GET /api/v1/me/companies", "listMyCompanies")]
    [TestCase("POST /api/v1/auth/password-reset/request", "requestPasswordReset")]
    [TestCase("POST /api/v1/auth/password-reset/confirm", "confirmPasswordReset")]
    [TestCase("POST /api/v1/companies/{nit}/{dv}", "createCompany")]
    [TestCase("PUT /api/v1/companies/{nit}/{dv}", "updateCompany")]
    [TestCase("PUT /api/v1/companies/{nit}/environment", "setCompanyEnvironment")]
    [TestCase("PUT /api/v1/companies/{nit}/software", "setCompanySoftware")]
    [TestCase("PUT /api/v1/companies/{nit}/certificate", "uploadCompanyCertificate")]
    [TestCase("PUT /api/v1/companies/{nit}/resolution", "saveCompanyResolution")]
    [TestCase("GET /api/v1/companies/{nit}/certificates", "listCompanyCertificates")]
    [TestCase("GET /api/v1/companies/{nit}/certificate/end-date", "getCertificateEndDate")]
    [TestCase("GET /api/v1/companies/{nit}/numbering-ranges", "getNumberingRanges")]
    [TestCase("PUT /api/v1/companies/{nit}/plan", "assignCompanyPlan")]
    [TestCase("GET /api/v1/information/{nit}", "listValidatedDocuments")]
    [TestCase("GET /api/v1/information/{nit}/{desde}", "listValidatedDocumentsOfDay")]
    [TestCase("GET /api/v1/information/{nit}/{desde}/{hasta}", "listValidatedDocumentsBetween")]
    [TestCase("GET /api/v1/information/{nit}/page/{page}/page", "listValidatedDocumentsPage")]
    [TestCase("GET /api/v1/total_documents/{nit}", "countValidatedDocuments")]
    [TestCase("GET /api/v1/total_documents/{nit}/{desde}", "countValidatedDocumentsOfDay")]
    [TestCase("GET /api/v1/total_documents/{nit}/{desde}/{hasta}", "countValidatedDocumentsBetween")]
    [TestCase("GET /api/v1/documents/current-number/{type}", "getNextDocumentNumber")]
    [TestCase("GET /api/v1/documents", "listDocuments")]
    [TestCase("GET /api/v1/documents/export.csv", "exportDocumentsCsv")]
    [TestCase("GET /api/v1/documents/{id}", "getDocument")]
    [TestCase("GET /api/v1/documents/by-cufe/{cufe}", "getDocumentByCufe")]
    [TestCase("POST /api/v1/documents/invoices", "issueInvoice")]
    [TestCase("POST /api/v1/documents/invoices/test-set/{testSetId}", "issueTestSetInvoice")]
    [TestCase("POST /api/v1/documents/credit-notes", "issueCreditNote")]
    [TestCase("POST /api/v1/documents/credit-notes/test-set/{testSetId}", "issueTestSetCreditNote")]
    [TestCase("POST /api/v1/documents/debit-notes", "issueDebitNote")]
    [TestCase("POST /api/v1/documents/debit-notes/test-set/{testSetId}", "issueTestSetDebitNote")]
    [TestCase("POST /api/v1/documents/calculate", "calculateDocumentTotals")]
    [TestCase("GET /api/v1/documents/by-cufe/{trackId}/dian-status", "getDocumentDianStatus")]
    [TestCase("GET /api/v1/documents/by-cufe/{trackId}/xml", "getDocumentXml")]
    [TestCase("GET /api/v1/documents/zip/{trackId}/dian-status", "getTestSetZipStatus")]
    [TestCase("GET /api/v1/documents/{id}/files/{kind}", "downloadDocumentFile")]
    [TestCase("POST /api/v1/documents/{id}/pdf/regenerate", "regenerateDocumentPdf")]
    [TestCase("POST /api/v1/documents/{id}/attached-document/regenerate", "regenerateAttachedDocument")]
    [TestCase("POST /api/v1/documents/{id}/email", "resendDocumentEmail")]
    [TestCase("GET /api/v1/plans", "listPlans")]
    [TestCase("GET /api/v1/plans/{id}", "getPlan")]
    [TestCase("PUT /api/v1/plans/{id}", "savePlan")]
    [TestCase("GET /api/v1/plans/{id}/companies", "listPlanCompanies")]
    [TestCase("GET /api/v1/plans/current", "getCurrentPlan")]
    [TestCase("GET /api/v1/notifications", "listNotifications")]
    [TestCase("POST /api/v1/notifications/{id}/read", "markNotificationRead")]
    [TestCase("POST /api/v1/notifications/read-all", "markAllNotificationsRead")]
    [TestCase("GET /api/v1/customers", "listCustomers")]
    [TestCase("GET /api/v1/customers/lookup", "lookupAcquirer")]
    [TestCase("GET /api/v1/customers/{id}", "getCustomer")]
    [TestCase("POST /api/v1/customers", "createCustomer")]
    [TestCase("PUT /api/v1/customers/{id}", "updateCustomer")]
    [TestCase("POST /api/v1/customers/{id}/deactivate", "deactivateCustomer")]
    [TestCase("GET /api/v1/products", "listProducts")]
    [TestCase("GET /api/v1/products/{id}", "getProduct")]
    [TestCase("POST /api/v1/products", "createProduct")]
    [TestCase("PUT /api/v1/products/{id}", "updateProduct")]
    [TestCase("POST /api/v1/products/{id}/deactivate", "deactivateProduct")]
    [TestCase("GET /api/v1/dashboard/summary", "getDashboardSummary")]
    public void An_operation_keeps_its_pinned_operation_id(string name, string operationId)
    {
        var operation = Operations().Where(candidate => candidate.Name == name).Select(candidate => candidate.Operation).ToList();

        Assert.That(operation, Has.Count.EqualTo(1), $"{name} is not documented.");
        Assert.That(Text(operation[0], "operationId"), Is.EqualTo(operationId));
    }

    /// <summary>
    /// Con <c>X-Client: panel</c> el cuerpo no trae <c>refresh_token</c> (va en la cookie). La respuesta 200 describe
    /// las dos formas, cada una con nombre propio, para que el cliente generado tipifique bien la del panel.
    /// </summary>
    [TestCase("POST /api/v1/auth/login")]
    [TestCase("POST /api/v1/auth/refresh")]
    public void The_session_operations_describe_the_integration_and_the_panel_responses(string name)
    {
        var operation = Operations().Single(candidate => candidate.Name == name).Operation;
        var schema = operation.GetProperty("responses").GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema");
        var references = schema.TryGetProperty("oneOf", out var oneOf)
            ? oneOf.EnumerateArray().Select(option => Text(option, "$ref")).ToList()
            : [];

        Assert.That(references, Is.EqualTo(new[] { "#/components/schemas/SessionTokens", "#/components/schemas/PanelSessionTokens" }));
    }

    [Test]
    public void The_panel_session_schema_has_no_refresh_token_and_the_integration_one_requires_it()
    {
        var schemas = _document.GetProperty("components").GetProperty("schemas");
        var panel = schemas.GetProperty("PanelSessionTokens");
        var integration = schemas.GetProperty("SessionTokens");

        Assert.Multiple(() =>
        {
            Assert.That(panel.GetProperty("properties").TryGetProperty("refresh_token", out _), Is.False);
            Assert.That(Names(panel, "required"), Is.EquivalentTo(new[]
            {
                "access_token", "expires_at", "refresh_token_expires_at", "company_id", "roles", "companies",
            }));
            Assert.That(Text(panel, "description"), Does.Contain("X-Client: panel"));
            Assert.That(Names(integration, "required"), Does.Contain("refresh_token"));
            Assert.That(Text(integration, "description"), Does.Contain("X-Client: panel"));
        });
    }

    /// <summary>
    /// Dos arranques de la API producen el mismo documento, byte a byte: el CI regenera el cliente y falla si cambia,
    /// así que un orden que varíe entre ejecuciones daría falsas alarmas.
    /// </summary>
    [Test]
    public async Task The_document_is_the_same_on_every_start()
    {
        using var other = new IdentityApiFactory();

        var again = await DocumentOf(other);

        Assert.That(again.GetRawText(), Is.EqualTo(_document.GetRawText()));
    }

    private static async Task<JsonElement> DocumentOf(IdentityApiFactory factory)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }

    private IEnumerable<(string Name, JsonElement Operation)> Operations() =>
        _document.GetProperty("paths").EnumerateObject()
            .Where(path => !path.Name.StartsWith(TestOnlyPrefix, StringComparison.Ordinal))
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(item => HttpMethods.Contains(item.Name))
                .Select(item => ($"{item.Name.ToUpperInvariant()} {path.Name}", item.Value)));

    private static IReadOnlyList<string> Names(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.EnumerateArray().Select(item => item.GetString()!).ToList() : [];

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
}
