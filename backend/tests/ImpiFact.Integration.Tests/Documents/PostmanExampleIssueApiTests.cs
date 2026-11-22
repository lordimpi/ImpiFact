using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImpiFact.Application.Abstractions;
using ImpiFact.Domain.Documents;
using ImpiFact.Integration.Tests.Companies;
using Microsoft.EntityFrameworkCore;

// Same namespace as the issue tests so that DocumentIssueHost (a namespace-scoped SetUpFixture) starts the API for it.
namespace ImpiFact.Integration.Tests.DocumentIssue;

/// <summary>
/// The invoice, credit note and debit note examples of the ApiDian Postman collection (folders 02, 04 and 07), kept in
/// <c>PostmanExamples/issue-examples.json</c> when the Postman replay was retired (phase 7a, T9b2; S2, item 7), issued
/// through <c>/api/v1/documents</c>. Each body goes as the collection has it, except what belonged to the installation
/// that recorded it: the number (the sequence of the resolution assigns it; the PHP accepted repeated numbers and
/// ImpiFact does not) and, in the notes, the invoice they reference (an accepted invoice of this company). The four
/// examples the replay recorded as known deviations answer 422 on the field that explains the decision.
/// </summary>
[Category("Integration")]
public sealed class PostmanExampleIssueApiTests
{
    private static readonly Lazy<IReadOnlyDictionary<string, PostmanExample>> All = new(Load);

    private Issuer? issuer;
    private (string FullNumber, string Cufe, string Date) reference;

    private Issuer Issuer => issuer ?? throw new InvalidOperationException("The issuer is not set up.");

    public static IEnumerable<TestCaseData> Examples() =>
        All.Value.Values.Select(example => new TestCaseData(example.Name).SetArgDisplayNames($"{example.Folder[..2]} {example.Name}"));

    [OneTimeSetUp]
    public async Task CreateIssuerAsync()
    {
        var (nit, dv) = CompaniesHost.NewNit();
        issuer = await IssuerSetup.CreateAsync(nit, dv);
        reference = await AcceptedInvoiceAsync();
    }

    [OneTimeTearDown]
    public void Release() => issuer?.Dispose();

    [Test]
    public void The_examples_of_the_collection_are_found() => Assert.That(All.Value, Has.Count.EqualTo(53));

    [TestCaseSource(nameof(Examples))]
    public async Task A_postman_example_is_issued_through_api_v1_like_the_replay_did(string name)
    {
        var example = All.Value[name];

        using var response = await PostAsync(IssueRoutes.Of(example.Route), Prepare(example));

        var text = await response.Content.ReadAsStringAsync();
        Assert.That((int)response.StatusCode, Is.EqualTo(example.Status), text);
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        if (example.Status != 200)
        {
            Assert.That(root.GetProperty("errors").TryGetProperty(example.ErrorField!, out _), Is.True, text);
            return;
        }

        var code = root.GetProperty(example.Route == "invoice" ? "cufe" : "cude").GetString();
        var document = root.GetProperty("document");
        Assert.Multiple(() =>
        {
            Assert.That(code, Has.Length.EqualTo(96));
            Assert.That(document.GetProperty("status").GetString(), Is.EqualTo("Queued"));
            Assert.That(document.GetProperty("document_type_id").GetInt32(), Is.EqualTo(TypeOf(example.Route)));
        });
        Assert.That(GoldenIssueTests.Verifies(await GoldenIssueTests.StoredSignedXmlAsync(document.GetProperty("id").GetGuid())), Is.True,
            "SignedXml verification of the stored XML.");
    }

    private byte[] Prepare(PostmanExample example)
    {
        var body = JsonNode.Parse(example.Body.GetRawText())!.AsObject();
        body.Remove("number");
        if (body["billing_reference"] is JsonObject billing)
        {
            billing["number"] = reference.FullNumber;
            billing["uuid"] = reference.Cufe;
            billing["issue_date"] = reference.Date;
        }

        return Encoding.UTF8.GetBytes(body.ToJsonString());
    }

    /// <summary>
    /// The invoice the notes reference. A note needs its invoice accepted by the DIAN; there is no Worker here, so it is set
    /// accepted as if the Worker had sent it.
    /// </summary>
    private async Task<(string FullNumber, string Cufe, string Date)> AcceptedInvoiceAsync()
    {
        var input = Path.Combine(GoldenIssueTests.GoldenRoot(), "invoice", "single-line-iva19", "input.json");
        var body = JsonNode.Parse(File.ReadAllBytes(input))!.AsObject();
        body.Remove("number");
        using var response = await PostAsync("/api/v1/documents/invoices", Encoding.UTF8.GetBytes(body.ToJsonString()));
        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
        using var json = JsonDocument.Parse(text);
        var document = json.RootElement.GetProperty("document");
        var id = document.GetProperty("id").GetGuid();
        await using (var scope = DocumentIssueHost.Api.OpenScope(null, null, ActorType.System))
        {
            await scope.Db.Set<Document>().IgnoreQueryFilters().Where(row => row.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Status, DocumentStatus.Accepted));
        }

        return (document.GetProperty("full_number").GetString()!, json.RootElement.GetProperty("cufe").GetString()!,
            body["date"]!.GetValue<string>());
    }

    private async Task<HttpResponseMessage> PostAsync(string path, byte[] body)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await Issuer.Client.PostAsync(path, content);
    }

    private static int TypeOf(string route) => route switch
    {
        "credit-note" => 4,
        "debit-note" => 5,
        _ => 1,
    };

    private static IReadOnlyDictionary<string, PostmanExample> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Documents", "PostmanExamples", "issue-examples.json");
        using var json = JsonDocument.Parse(File.ReadAllBytes(path));
        return json.RootElement.EnumerateArray()
            .Select(row => new PostmanExample(
                row.GetProperty("name").GetString()!,
                row.GetProperty("folder").GetString()!,
                row.GetProperty("route").GetString()!,
                row.GetProperty("status").GetInt32(),
                row.GetProperty("error_field").GetString(),
                row.GetProperty("body").Clone()))
            .ToDictionary(example => example.Name, StringComparer.Ordinal);
    }

    /// <param name="Route">The PHP route of the collection: <c>invoice</c>, <c>credit-note</c> or <c>debit-note</c>.</param>
    /// <param name="Status">200, or 422 for the examples the replay recorded as known deviations.</param>
    /// <param name="ErrorField">The field of the 422 (<c>errors</c>), with the decision documented in php-rules.</param>
    private sealed record PostmanExample(string Name, string Folder, string Route, int Status, string? ErrorField, JsonElement Body);
}
