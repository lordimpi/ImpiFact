using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Documents;
using ImpiFact.Integration.Tests.DianSend;
using static ImpiFact.Integration.Tests.DocumentUtilities.UtilitiesHost;

namespace ImpiFact.Integration.Tests.DocumentUtilities;

/// <summary>
/// <c>GET download/{nit}/{file}/{type_response?}</c> (carpeta 05 del Postman) y su alias
/// <c>GET /api/v1/documents/{id}/files/{kind}</c>: los archivos guardados con los nombres del PHP, solo de la empresa
/// de la credencial.
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
        var (company, client, document) = await AcceptedWithFilesAsync();

        using var response = await client.GetAsync($"/api/ubl2.1/download/{company.Nit}/FES-{document.FullNumber}.pdf");

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
    public async Task Base64_answers_the_php_json()
    {
        var (company, client, document) = await AcceptedWithFilesAsync();
        var file = $"Attachment-{document.FullNumber}.xml";

        var (status, body) = await SendAsync(client, HttpMethod.Get, $"/api/ubl2.1/download/{company.Nit}/{file}/BASE64");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(200));
            Assert.That(body.GetRawText(), Is.EqualTo(
                $$"""{"success":true,"message":"Archivo: {{file}} se encontro.","filebase64":"{{Convert.ToBase64String(Attached)}}"}"""));
        });
    }

    [Test]
    public async Task Inline_shows_the_file_without_a_download_name()
    {
        var (company, client, document) = await AcceptedWithFilesAsync();

        using var response = await client.GetAsync($"/api/download/{company.Nit}/FES-{document.FullNumber}.xml/INLINE");

        Assert.Multiple(async () =>
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(200));
            Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/xml"));
            Assert.That(response.Content.Headers.ContentDisposition, Is.Null);
            Assert.That(Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync()), Is.EqualTo(DianSendHost.SignedXml(document)));
        });
    }

    [Test]
    public async Task The_email_zip_carries_the_attached_document_and_the_pdf()
    {
        var (company, client, document) = await AcceptedWithFilesAsync();

        using var response = await client.GetAsync($"/api/ubl2.1/download/{company.Nit}/ZipAttachm-{document.FullNumber}.zip");

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

        using var wrongTag = await client.GetAsync($"/api/ubl2.1/download/{company.Nit}/FES-{note.FullNumber}.xml");
        using var response = await client.GetAsync($"/api/ubl2.1/download/{company.Nit}/NCS-{note.FullNumber}.xml");

        Assert.Multiple(() =>
        {
            Assert.That((int)wrongTag.StatusCode, Is.EqualTo(404));
            Assert.That((int)response.StatusCode, Is.EqualTo(200));
        });
    }

    [TestCase("notes.txt")]
    [TestCase("FES-SETP1.pdf")]
    [TestCase("RptaFE-SETP1.xml")]
    public async Task An_unknown_file_answers_the_php_text(string file)
    {
        var company = await CompanyAsync();
        var client = await ClientAsync(company, ApiScopes.DocumentsRead);

        var (status, body) = await SendAsync(client, HttpMethod.Get, $"/api/ubl2.1/download/{company.Nit}/{file}");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(404));
            Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("document.file_not_found"));
            Assert.That(body.GetProperty("message").GetString(), Is.EqualTo($"No se encontro el archivo: {file}"));
        });
    }

    [Test]
    public async Task A_stored_file_that_is_missing_answers_not_found()
    {
        var company = await CompanyAsync();
        var client = await ClientAsync(company, ApiScopes.DocumentsRead);
        var document = await DocumentAsync(company);

        var (status, _) = await SendAsync(client, HttpMethod.Get, $"/api/ubl2.1/download/{company.Nit}/FES-{document.FullNumber}.pdf");

        Assert.That(status, Is.EqualTo(404));
    }

    [Test]
    public async Task Another_company_nit_is_forbidden_and_its_files_are_not_found_under_the_own_nit()
    {
        var (company, client, _) = await AcceptedWithFilesAsync();
        var (other, _, foreign) = await AcceptedWithFilesAsync();

        var (forbidden, body) = await SendAsync(client, HttpMethod.Get, $"/api/ubl2.1/download/{other.Nit}/FES-{foreign.FullNumber}.pdf");
        var (hidden, _) = await SendAsync(client, HttpMethod.Get, $"/api/ubl2.1/download/{company.Nit}/FES-{foreign.FullNumber}.pdf");
        var (byId, _) = await SendAsync(client, HttpMethod.Get, $"/api/v1/documents/{foreign.Id}/files/pdf");

        Assert.Multiple(() =>
        {
            Assert.That(forbidden, Is.EqualTo(403));
            Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("company.forbidden"));
            Assert.That(hidden, Is.EqualTo(404));
            Assert.That(byId, Is.EqualTo(404));
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

        var (status, _) = await SendAsync(client, HttpMethod.Get, $"/api/ubl2.1/download/{company.Nit}/FES-{document.FullNumber}.xml");

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
