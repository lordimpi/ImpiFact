using System.IO.Compression;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Documents;
using static ImpiFact.Integration.Tests.DocumentUtilities.UtilitiesHost;

namespace ImpiFact.Integration.Tests.DocumentUtilities;

/// <summary>
/// <c>GET /api/v1/documents/{id}/files/{kind}</c> (el <c>download/{nit}/{file}</c> del PHP, carpeta 05 del Postman): los
/// archivos guardados con los nombres del PHP, solo de la empresa de la credencial.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class DownloadApiTests
{
    private static readonly byte[] OldPdf = "%PDF-1.7 old"u8.ToArray();
    private static readonly byte[] Pdf = "%PDF-1.7 regenerated"u8.ToArray();
    private static readonly byte[] Attached = "<AttachedDocument/>"u8.ToArray();

    [Test]
    public async Task The_pdf_is_downloaded_with_its_php_name_in_its_latest_revision()
    {
        var (_, client, document) = await AcceptedWithFilesAsync();

        using var response = await client.GetAsync($"/api/v1/documents/{document.Id}/files/pdf");

        Assert.Multiple(async () =>
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(200));
            Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/pdf"));
            Assert.That(response.Content.Headers.ContentDisposition!.DispositionType, Is.EqualTo("attachment"));
            Assert.That(response.Content.Headers.ContentDisposition.FileName, Is.EqualTo($"FES-{document.FullNumber}.pdf"));
            Assert.That(await response.Content.ReadAsByteArrayAsync(), Is.EqualTo(Pdf));
        });
    }

    [Test]
    public async Task The_email_zip_carries_the_attached_document_and_the_pdf()
    {
        var (_, client, document) = await AcceptedWithFilesAsync();

        using var response = await client.GetAsync($"/api/v1/documents/{document.Id}/files/zip");

        using var zip = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read);
        Assert.Multiple(() =>
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(200));
            Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/zip"));
            Assert.That(zip.Entries.Select(entry => entry.FullName), Is.EqualTo(new[] { $"ad-{document.FullNumber}.xml", $"ad-{document.FullNumber}.pdf" }));
        });
    }

    [Test]
    public async Task A_credit_note_uses_its_own_tag()
    {
        var company = await CompanyAsync();
        var client = await ClientAsync(company, ApiScopes.DocumentsRead);
        var note = await DocumentAsync(company, documentTypeId: 4, prefix: "NC");

        using var response = await client.GetAsync($"/api/v1/documents/{note.Id}/files/xml");

        Assert.Multiple(() =>
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(200));
            Assert.That(response.Content.Headers.ContentDisposition!.FileName, Is.EqualTo($"NCS-{note.FullNumber}.xml"));
        });
    }

    [Test]
    public async Task A_stored_file_that_is_missing_answers_not_found()
    {
        var company = await CompanyAsync();
        var client = await ClientAsync(company, ApiScopes.DocumentsRead);
        var document = await DocumentAsync(company);

        var (status, body) = await SendAsync(client, HttpMethod.Get, $"/api/v1/documents/{document.Id}/files/pdf");

        Assert.That(status, Is.EqualTo(404));
        Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("document.file_not_found"));
    }

    [Test]
    public async Task The_files_of_another_company_are_not_found()
    {
        var (_, client, _) = await AcceptedWithFilesAsync();
        var (_, _, foreign) = await AcceptedWithFilesAsync();

        var (byId, body) = await SendAsync(client, HttpMethod.Get, $"/api/v1/documents/{foreign.Id}/files/pdf");

        Assert.Multiple(() =>
        {
            Assert.That(byId, Is.EqualTo(404));
            Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("document.not_found"));
        });
    }

    [Test]
    public async Task The_canonical_route_downloads_each_kind_by_document_id()
    {
        var (_, client, document) = await AcceptedWithFilesAsync();

        using var pdf = await client.GetAsync($"/api/v1/documents/{document.Id}/files/pdf");
        using var attached = await client.GetAsync($"/api/v1/documents/{document.Id}/files/attached-document");
        using var xml = await client.GetAsync($"/api/v1/documents/{document.Id}/files/xml");
        using var zip = await client.GetAsync($"/api/v1/documents/{document.Id}/files/zip");
        var (unknown, _) = await SendAsync(client, HttpMethod.Get, $"/api/v1/documents/{document.Id}/files/request");

        Assert.Multiple(async () =>
        {
            Assert.That(await pdf.Content.ReadAsByteArrayAsync(), Is.EqualTo(Pdf));
            Assert.That(pdf.Content.Headers.ContentDisposition!.FileName, Is.EqualTo($"FES-{document.FullNumber}.pdf"));
            Assert.That(await attached.Content.ReadAsByteArrayAsync(), Is.EqualTo(Attached));
            Assert.That(attached.Content.Headers.ContentDisposition!.FileName, Is.EqualTo($"Attachment-{document.FullNumber}.xml"));
            Assert.That(xml.Content.Headers.ContentDisposition!.FileName, Is.EqualTo($"FES-{document.FullNumber}.xml"));
            Assert.That(zip.Content.Headers.ContentDisposition!.FileName, Is.EqualTo($"ZipAttachm-{document.FullNumber}.zip"));
            Assert.That(unknown, Is.EqualTo(404));
        });
    }

    [Test]
    public async Task Downloads_need_the_documents_read_scope()
    {
        var company = await CompanyAsync();
        var client = await ClientAsync(company, ApiScopes.DocumentsWrite);
        var document = await DocumentAsync(company);

        var (status, _) = await SendAsync(client, HttpMethod.Get, $"/api/v1/documents/{document.Id}/files/xml");

        Assert.That(status, Is.EqualTo(403));
    }

    private static async Task<(UtilityCompany Company, HttpClient Client, Document Document)> AcceptedWithFilesAsync()
    {
        var company = await CompanyAsync(withCertificate: false);
        var client = await ClientAsync(company, ApiScopes.DocumentsRead);
        var document = await DocumentAsync(company);
        await FileAsync(document, DocumentFileKind.Pdf, OldPdf, "application/pdf");
        await FileAsync(document, DocumentFileKind.Pdf, Pdf, "application/pdf", revision: 2);
        await FileAsync(document, DocumentFileKind.AttachedDocument, Attached, "application/xml");
        return (company, client, document);
    }
}
