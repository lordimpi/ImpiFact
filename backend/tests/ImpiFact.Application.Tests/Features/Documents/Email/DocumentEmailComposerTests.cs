using System.IO.Compression;
using System.Text;
using ImpiFact.Application.Features.Documents.Email;

namespace ImpiFact.Application.Tests.Features.Documents.Email;

/// <summary>
/// Asunto, adjunto y texto del correo del documento aceptado, como ApiDian: asunto
/// <c>NIT;nombre;prefijonúmero;código del tipo;nombre</c> (InvoiceMail), un ZIP <c>{ad…}.zip</c> con el
/// AttachedDocument y el PDF renombrado <c>{ad…}.pdf</c> (DocumentTrait::zipEmail) y el mensaje de
/// <c>mails/mail.blade.php</c>.
/// </summary>
public sealed class DocumentEmailComposerTests
{
    private const string AdName = "ad08200000010002600000001";

    [Test]
    public void The_subject_is_built_exactly_as_ApiDian_builds_it() =>
        Assert.That(DocumentEmailComposer.Subject("900000001", "EMPRESA SAS", "SETP990000001", "01"),
            Is.EqualTo("900000001;EMPRESA SAS;SETP990000001;01;EMPRESA SAS"));

    [Test]
    public void The_attached_document_name_comes_from_the_xml_sent_to_the_DIAN() =>
        Assert.That(DocumentEmailComposer.AttachedDocumentName(null, Zip("fv08200000010002600000001.xml"), "SETP990000001"),
            Is.EqualTo(AdName));

    [TestCase("nc08200000010002600000003.xml", "ad08200000010002600000003")]
    [TestCase("nd08200000010002600000004.xml", "ad08200000010002600000004")]
    public void Notes_get_the_ad_prefix_too(string entry, string expected) =>
        Assert.That(DocumentEmailComposer.AttachedDocumentName(null, Zip(entry), "NC1"), Is.EqualTo(expected));

    // The PHP replaces dse and then nd → ad and ads → ad (SupportDocumentController 364, SdCreditNoteController 330).
    [TestCase("dse08200000010002600000005.xml", "ad08200000010002600000005")]
    [TestCase("nds08200000010002600000006.xml", "ad08200000010002600000006")]
    public void The_support_document_and_its_adjustment_note_get_the_ad_prefix_too(string entry, string expected) =>
        Assert.That(DocumentEmailComposer.AttachedDocumentName(null, Zip(entry), "SEDS1"), Is.EqualTo(expected));

    [Test]
    public void Atacheddocument_name_prefix_goes_before_the_name() =>
        Assert.That(DocumentEmailComposer.AttachedDocumentName("PRE-", Zip("fv08200000010002600000001.xml"), "SETP990000001"),
            Is.EqualTo("PRE-" + AdName));

    [Test]
    public void Without_a_sent_zip_the_name_falls_back_to_the_document_number() =>
        Assert.That(DocumentEmailComposer.AttachedDocumentName(null, null, "SETP990000001"), Is.EqualTo("ad-SETP990000001"));

    [Test]
    public void The_attachment_is_a_zip_with_the_attached_document_and_the_pdf_under_the_same_name()
    {
        var xml = Encoding.UTF8.GetBytes("<AttachedDocument/>");
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7");

        var attachment = DocumentEmailComposer.Attachment(AdName, xml, pdf);

        using var archive = new ZipArchive(new MemoryStream(attachment.Content), ZipArchiveMode.Read);
        Assert.Multiple(() =>
        {
            Assert.That(attachment.FileName, Is.EqualTo(AdName + ".zip"));
            Assert.That(attachment.ContentType, Is.EqualTo("application/zip"));
            Assert.That(archive.Entries.Select(entry => entry.FullName), Is.EqualTo(new[] { AdName + ".xml", AdName + ".pdf" }));
            Assert.That(Read(archive.GetEntry(AdName + ".xml")!), Is.EqualTo(xml));
            Assert.That(Read(archive.GetEntry(AdName + ".pdf")!), Is.EqualTo(pdf));
        });
    }

    [Test]
    public void The_body_greets_the_buyer_and_lists_the_document_like_the_PHP_view()
    {
        var (html, text) = DocumentEmailComposer.Body(Content(), DocumentEmailRequest.None);

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("<title>Notificación de Comprobante Electrónico Nro SETP-990000001</title>"));
            Assert.That(html, Does.Contain("Señor(es), CLIENTE &amp; HIJOS identificado con NIT 800000001"));
            Assert.That(html, Does.Contain("ha recibido un documento electrónico de EMPRESA SAS."));
            Assert.That(html, Does.Contain("<p>SETP990000001</p>"));
            Assert.That(html, Does.Contain("<p>2026-03-02 08:00:00</p>"));
            Assert.That(html, Does.Contain("<p>1,234,567.89</p>"));
            Assert.That(html, Does.Contain("href=\"https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=abc123\""));
            Assert.That(html, Does.Contain("Verificar en la DIAN"));
            Assert.That(text, Does.Contain("Señor(es), CLIENTE & HIJOS identificado con NIT 800000001"));
            Assert.That(text, Does.Contain("SETP990000001"));
            Assert.That(text, Does.Contain("1,234,567.89"));
            Assert.That(text, Does.Contain("https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=abc123"));
        });
    }

    [Test]
    public void The_html_of_the_request_replaces_each_part_of_the_body()
    {
        var request = DocumentEmailRequest.None with
        {
            HtmlHeader = "<h1>Own header</h1>", HtmlBody = "<p>Own body</p>", HtmlButtons = "<a>Own button</a>", HtmlFooter = "<p>Own footer</p>",
        };

        var (html, _) = DocumentEmailComposer.Body(Content(), request);

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("<h1>Own header</h1>"));
            Assert.That(html, Does.Contain("<p>Own body</p>"));
            Assert.That(html, Does.Contain("<a>Own button</a>"));
            Assert.That(html, Does.Contain("<p>Own footer</p>"));
            Assert.That(html, Does.Not.Contain("Señor(es)"));
            Assert.That(html, Does.Not.Contain("1,234,567.89"));
        });
    }

    private static DocumentEmailContent Content() => new(
        CompanyName: "EMPRESA SAS",
        CustomerName: "CLIENTE & HIJOS",
        CustomerIdentification: "800000001",
        Prefix: "SETP",
        Number: 990000001,
        FullNumber: "SETP990000001",
        IssueDate: new DateOnly(2026, 3, 2),
        IssueTime: new TimeOnly(8, 0, 0),
        Total: 1234567.891m,
        DianQueryUrl: "https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=abc123");

    private static byte[] Zip(string entryName)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var stream = archive.CreateEntry(entryName).Open();
            stream.Write("<Invoice/>"u8);
        }

        return buffer.ToArray();
    }

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
