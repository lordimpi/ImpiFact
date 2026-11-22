using System.Net;
using System.Text;
using System.Text.Json;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Documents;
using ImpiFact.Integration.Tests.DocumentIssue;
using static ImpiFact.Integration.Tests.DocumentUtilities.UtilitiesHost;

namespace ImpiFact.Integration.Tests.DocumentUtilities;

/// <summary>
/// <c>POST /api/v1/documents/pdf/merge</c> (RF-12 «unir PDFs», the <c>join-pdfs</c> of the PHP): the latest PDF of each
/// document of the company, in the order asked for, merged into one PDF by Gotenberg (a fake here) with its
/// <c>/forms/pdfengines/merge</c> route.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class MergePdfApiTests
{
    private const string Route = "/api/v1/documents/pdf/merge";

    private static FakeGotenberg Gotenberg => UtilitiesHost.Api.Gotenberg;

    [TearDown]
    public void GotenbergUp() => Gotenberg.Down = false;

    [Test]
    public async Task The_pdfs_are_merged_in_the_order_asked_for_with_their_latest_revision()
    {
        var company = await CompanyAsync(withCertificate: false);
        var first = await WithPdfAsync(company, "%PDF-1.7 first");
        var second = await WithPdfAsync(company, "%PDF-1.7 second");
        var third = await WithPdfAsync(company, "%PDF-1.7 third old");
        await FileAsync(third, DocumentFileKind.Pdf, "%PDF-1.7 third regenerated"u8.ToArray(), "application/pdf", revision: 2);
        using var client = await ClientAsync(company, ApiScopes.DocumentsRead);
        var before = Gotenberg.Conversions.Count;

        using var response = await PostAsync(client, third.Id, first.Id, second.Id);

        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), Encoding.UTF8.GetString(body));
        var merge = Gotenberg.Conversions.Skip(before).Single(conversion => conversion.Files.Values.Any(file => Text(file.Content) == "%PDF-1.7 first"));
        Assert.Multiple(() =>
        {
            Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/pdf"));
            Assert.That(response.Content.Headers.ContentDisposition!.FileName?.Trim('"'), Is.EqualTo("documentos-unidos.pdf"));
            Assert.That(body, Is.EqualTo(FakeGotenberg.Pdf));
            Assert.That(merge.Path, Does.EndWith("/forms/pdfengines/merge"));
            // Gotenberg merges in the alphabetical order of the file names.
            Assert.That(merge.Files.OrderBy(file => file.Key, StringComparer.Ordinal).Select(file => Text(file.Value.Content)),
                Is.EqualTo(new[] { "%PDF-1.7 third regenerated", "%PDF-1.7 first", "%PDF-1.7 second" }));
            Assert.That(merge.Files.Values.Select(file => file.ContentType), Is.All.EqualTo("application/pdf"));
        });
    }

    [Test]
    public async Task A_document_of_another_company_answers_404_for_the_whole_request()
    {
        var company = await CompanyAsync(withCertificate: false);
        var other = await CompanyAsync(withCertificate: false);
        var own = await WithPdfAsync(company, "%PDF-1.7 own");
        var foreign = await WithPdfAsync(other, "%PDF-1.7 foreign");
        using var client = await ClientAsync(company, ApiScopes.DocumentsRead);
        var before = Gotenberg.Conversions.Count;

        using var response = await PostAsync(client, own.Id, foreign.Id);
        using var unknown = await PostAsync(client, own.Id, Guid.CreateVersion7());

        using var json = await JsonAsync(response);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(json.RootElement.GetProperty("code").GetString(), Is.EqualTo("document.not_found"));
            Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(Gotenberg.Conversions.Count, Is.EqualTo(before), "Nothing is merged.");
        });
    }

    [Test]
    public async Task Documents_without_a_pdf_answer_422_naming_them()
    {
        var company = await CompanyAsync(withCertificate: false);
        var withPdf = await WithPdfAsync(company, "%PDF-1.7 ready");
        var queued = await DocumentAsync(company, DocumentStatus.Queued);
        var rejected = await DocumentAsync(company, DocumentStatus.Rejected);
        using var client = await ClientAsync(company, ApiScopes.DocumentsRead);
        var before = Gotenberg.Conversions.Count;

        using var response = await PostAsync(client, withPdf.Id, queued.Id, rejected.Id);

        using var json = await JsonAsync(response);
        var expected = $"Estos documentos no tienen PDF: {queued.FullNumber}, {rejected.FullNumber}.";
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), json.RootElement.ToString());
            Assert.That(json.RootElement.GetProperty("code").GetString(), Is.EqualTo("documents.pdf_missing"));
            Assert.That(json.RootElement.GetProperty("detail").GetString(), Is.EqualTo(expected));
            Assert.That(json.RootElement.GetProperty("errors").GetProperty("document_ids")[0].GetString(), Is.EqualTo(expected));
            Assert.That(Gotenberg.Conversions.Count, Is.EqualTo(before));
        });
    }

    [TestCase(0, TestName = "An_empty_list_answers_422")]
    [TestCase(51, TestName = "More_than_50_documents_answer_422")]
    public async Task The_list_has_between_1_and_50_documents(int count)
    {
        var company = await CompanyAsync(withCertificate: false);
        using var client = await ClientAsync(company, ApiScopes.DocumentsRead);

        using var response = await PostAsync(client, [.. Enumerable.Range(0, count).Select(_ => Guid.CreateVersion7())]);

        using var json = await JsonAsync(response);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), json.RootElement.ToString());
            Assert.That(json.RootElement.GetProperty("errors").GetProperty("document_ids")[0].GetString(),
                Is.EqualTo("Envíe entre 1 y 50 documentos para unir sus PDF."));
        });
    }

    [Test]
    public async Task A_converter_failure_answers_503_pdf_unavailable()
    {
        var company = await CompanyAsync(withCertificate: false);
        var first = await WithPdfAsync(company, "%PDF-1.7 a");
        var second = await WithPdfAsync(company, "%PDF-1.7 b");
        using var client = await ClientAsync(company, ApiScopes.DocumentsRead);
        Gotenberg.Down = true;

        using var response = await PostAsync(client, first.Id, second.Id);

        using var json = await JsonAsync(response);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(json.RootElement.GetProperty("code").GetString(), Is.EqualTo("pdf.unavailable"));
        });
    }

    [Test]
    public async Task A_single_document_answers_its_own_pdf_without_calling_the_converter()
    {
        var company = await CompanyAsync(withCertificate: false);
        var only = await WithPdfAsync(company, "%PDF-1.7 alone");
        using var client = await ClientAsync(company, ApiScopes.DocumentsRead);
        var before = Gotenberg.Conversions.Count;

        using var response = await PostAsync(client, only.Id);

        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Text(await response.Content.ReadAsByteArrayAsync()), Is.EqualTo("%PDF-1.7 alone"));
            Assert.That(Gotenberg.Conversions.Count, Is.EqualTo(before));
        });
    }

    [Test]
    public async Task Merging_needs_documents_read()
    {
        var company = await CompanyAsync(withCertificate: false);
        var only = await WithPdfAsync(company, "%PDF-1.7 scoped");
        using var configOnly = await ClientAsync(company, ApiScopes.ConfigWrite);
        using var anonymous = UtilitiesHost.Api.CreateClient();

        using var forbidden = await PostAsync(configOnly, only.Id);
        using var unauthenticated = await PostAsync(anonymous, only.Id);

        Assert.Multiple(() =>
        {
            Assert.That(forbidden.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(unauthenticated.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        });
    }

    private static async Task<Document> WithPdfAsync(UtilityCompany company, string pdf)
    {
        var document = await DocumentAsync(company);
        await FileAsync(document, DocumentFileKind.Pdf, Encoding.ASCII.GetBytes(pdf), "application/pdf");
        return document;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, params Guid[] ids) =>
        client.PostAsync(Route, new StringContent(JsonSerializer.Serialize(new { document_ids = ids }), Encoding.UTF8, "application/json"));

    private static async Task<JsonDocument> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static string Text(byte[] content) => Encoding.ASCII.GetString(content);
}
