using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Domain.Documents;
using ImpiFact.Integration.Tests.Companies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.DocumentIssue;

/// <summary>
/// Puerta de la fase 3 de punta a punta: «Golden tests: XML y CUFE idénticos al PHP en factura y notas». La empresa
/// del contexto de captura (NIT 900000001, resolución SETP 990000000–995000000 con su clave técnica, PIN 12345,
/// habilitación) se crea por la API de Company, cada input.json de tests/golden se envía tal cual a su ruta de emisión
/// de /api/v1 y se comparan byte a byte el XML sin firmar de la respuesta y el CUFE o CUDE (códigos únicos de factura y de
/// documento electrónico) con lo que generó el PHP. El único ajuste es el del lugar vacío de la firma.
/// </summary>
[Category("Integration")]
public sealed partial class GoldenIssueTests
{
    private const string SignatureSlotInFixture = "<ext:ExtensionContent></ext:ExtensionContent>";
    private const string DsNs = "http://www.w3.org/2000/09/xmldsig#";

    private static Issuer? golden;

    private static Issuer Golden => golden ?? throw new InvalidOperationException("The golden company is not set up.");

    public static IEnumerable<TestCaseData> Cases() =>
        new[] { ("invoice", "invoice"), ("credit-note", "credit-note"), ("debit-note", "debit-note") }
            .SelectMany(pair => Directory.EnumerateDirectories(Path.Combine(GoldenRoot(), pair.Item1)).Order(StringComparer.Ordinal)
                .Select(dir => new TestCaseData(pair.Item2, dir).SetArgDisplayNames(pair.Item1 + "/" + Path.GetFileName(dir))));

    [OneTimeSetUp]
    public async Task CreateGoldenCompanyAsync() => golden = await IssuerSetup.CreateAsync("900000001", "2");

    [OneTimeTearDown]
    public void Release() => golden?.Dispose();

    [Test]
    public void All_fourteen_golden_cases_are_found() => Assert.That(Cases().Count(), Is.EqualTo(14));

    // Va primero: la prueba del enlace repite dos de estos casos, y una repetición no trae el XML sin firmar.
    [TestCaseSource(nameof(Cases))]
    [Order(1)]
    public async Task Golden_input_posted_to_the_issue_route_issues_the_php_xml_and_code(string route, string caseDir)
    {
        using var response = await PostAsync(route, caseDir);

        var body = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var expectedCode = File.ReadAllText(Path.Combine(caseDir, "expected.cufe")).Trim();
        Assert.That(root.GetProperty(route == "invoice" ? "cufe" : "cude").GetString(), Is.EqualTo(expectedCode));

        var expected = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(caseDir, "expected.xml")));
        var unsigned = Encoding.UTF8.GetString(Convert.FromBase64String(root.GetProperty("unsignedinvoicexml").GetString()!));
        Assert.That(unsigned, Is.EqualTo(expected.Replace(SignatureSlotInFixture, "<ext:ExtensionContent/>", StringComparison.Ordinal)));

        // El XML guardado (document_files de tipo 1) es el firmado: su firma verifica y, sin ella, es el del PHP.
        var documentId = root.GetProperty("document").GetProperty("id").GetGuid();
        var stored = await StoredSignedXmlAsync(documentId);
        Assert.That(SignatureElement().Replace(stored, ""), Is.EqualTo(expected), "Stored signed XML without its signature.");
        Assert.That(Verifies(stored), Is.True, "SignedXml verification of the stored XML.");
        Assert.That(Convert.FromBase64String(root.GetProperty("invoicexml").GetString()!), Is.EqualTo(Encoding.UTF8.GetBytes(stored)));
    }

    // Phase 7a, T8: the PHP has no batch golden of its own (T1: «lotes no: nada propio reproducible»), so a document added to a
    // batch is checked against the golden of its type: the variants that no other test of the golden company issues go into one
    // batch through /api/v1/documents/batches/{id}/documents/{type} and give the PHP XML and code, signed and Queued in the
    // batch.
    [TestCase("invoice-aiu", "construction-contract")]
    [TestCase("invoice-mandate", "third-party-line")]
    [Order(0)]
    public async Task Golden_input_added_to_a_batch_issues_the_php_xml_and_code(string type, string goldenCase)
    {
        var caseDir = Path.Combine(GoldenRoot(), type, goldenCase);
        var content = new ByteArrayContent(File.ReadAllBytes(Path.Combine(caseDir, "input.json")));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var batch = await GoldenBatchAsync();
        using var response = await Golden.Client.PostAsync($"/api/v1/documents/batches/{batch}/documents/{type}", content);

        var body = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var expected = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(caseDir, "expected.xml")));
        var unsigned = Encoding.UTF8.GetString(Convert.FromBase64String(root.GetProperty("unsignedinvoicexml").GetString()!));
        var documentId = root.GetProperty("document").GetProperty("id").GetGuid();
        var stored = await StoredSignedXmlAsync(documentId);
        await using var scope = DocumentIssueHost.Api.OpenScope(null, null, ActorType.System);
        var row = await CompanyApiTests.Rows<Document>(scope.Db, Golden.CompanyId).SingleAsync(document => document.Id == documentId);
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("cufe").GetString(), Is.EqualTo(File.ReadAllText(Path.Combine(caseDir, "expected.cufe")).Trim()));
            Assert.That(unsigned, Is.EqualTo(expected.Replace(SignatureSlotInFixture, "<ext:ExtensionContent/>", StringComparison.Ordinal)));
            Assert.That(SignatureElement().Replace(stored, ""), Is.EqualTo(expected), "Stored signed XML without its signature.");
            Assert.That(Verifies(stored), Is.True, "SignedXml verification of the stored XML.");
            Assert.That((row.Status, row.BatchId), Is.EqualTo((DocumentStatus.Queued, (Guid?)root.GetProperty("document").GetProperty("batch_id").GetGuid())));
        });
    }

    // Supuesto B8: la nota golden referencia la factura golden SETP990000001 por su CUFE, así que queda enlazada.
    [Test]
    [Order(2)]
    public async Task Golden_credit_note_is_linked_to_the_golden_invoice_it_references()
    {
        var invoiceDir = Path.Combine(GoldenRoot(), "invoice", "two-lines-discounts-iva19");
        var noteDir = Path.Combine(GoldenRoot(), "credit-note", "partial-return-with-reference");
        using var invoice = await PostAsync("invoice", invoiceDir);
        using var note = await PostAsync("credit-note", noteDir);

        Assert.That(invoice.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(note.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await using var scope = DocumentIssueHost.Api.OpenScope(null, null, ActorType.System);
        var documents = CompanyApiTests.Rows<Document>(scope.Db, Golden.CompanyId);
        var invoiceRow = await documents.SingleAsync(row => row.DocumentTypeId == 1 && row.Number == 990000001);
        var noteRow = await documents.SingleAsync(row => row.DocumentTypeId == 4 && row.Number == 1);
        Assert.That(noteRow.ReferenceDocumentId, Is.EqualTo(invoiceRow.Id));
        Assert.That(invoiceRow.Cufe, Is.EqualTo(File.ReadAllText(Path.Combine(invoiceDir, "expected.cufe")).Trim()));
    }

    private static async Task<HttpResponseMessage> PostAsync(string route, string caseDir)
    {
        var content = new ByteArrayContent(File.ReadAllBytes(Path.Combine(caseDir, "input.json")));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var response = await Golden.Client.PostAsync(IssueRoutes.Of(route), content);
        if (route == "invoice" && response.IsSuccessStatusCode)
        {
            // Desde la fase 4 una nota exige que su factura ya esté aceptada por la DIAN. Aquí no hay DIAN ni Worker, así
            // que la factura emitida se da por aceptada, como si el Worker ya la hubiera enviado.
            await using var scope = DocumentIssueHost.Api.OpenScope(null, null, ActorType.System);
            await scope.Db.Set<Document>().IgnoreQueryFilters()
                .Where(row => row.CompanyId == Golden.CompanyId && row.DocumentTypeId == 1 && row.Status == DocumentStatus.Queued)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Status, DocumentStatus.Accepted));
        }

        return response;
    }

    private static Guid? goldenBatch;

    /// <summary>The open batch «lote-golden» of the golden company, created by the first case that needs it.</summary>
    private static async Task<Guid> GoldenBatchAsync()
    {
        if (goldenBatch is { } id)
        {
            return id;
        }

        var content = new StringContent("""{"name":"lote-golden"}""", Encoding.UTF8, "application/json");
        using var response = await Golden.Client.PostAsync("/api/v1/documents/batches", content);
        var body = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), body);
        using var json = JsonDocument.Parse(body);
        goldenBatch = json.RootElement.GetProperty("id").GetGuid();
        return goldenBatch.Value;
    }

    internal static async Task<string> StoredSignedXmlAsync(Guid documentId)
    {
        await using var scope = DocumentIssueHost.Api.OpenScope(null, null, ActorType.System);
        var file = await scope.Db.Set<DocumentFile>().IgnoreQueryFilters()
            .SingleAsync(row => row.DocumentId == documentId && row.Kind == DocumentFileKind.SignedXml);
        await using var stream = await scope.Services.GetRequiredService<IBlobStorage>().OpenReadAsync(file.StorageKey, CancellationToken.None);
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        return await reader.ReadToEndAsync();
    }

    internal static bool Verifies(string signedXml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        using (var reader = XmlReader.Create(new StringReader(signedXml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
        {
            doc.Load(reader);
        }

        var signature = (XmlElement)doc.GetElementsByTagName("Signature", DsNs)[0]!;
        var checker = new SignedXml(doc);
        checker.LoadXml(signature);
        return checker.CheckSignature();
    }

    internal static string GoldenRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "golden");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("tests/golden not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex(@"<ds:Signature\b.*?</ds:Signature>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex SignatureElement();
}
