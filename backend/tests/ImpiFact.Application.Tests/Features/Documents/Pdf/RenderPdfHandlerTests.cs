using System.Text.Json;
using System.Text.Json.Nodes;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Abstractions.Pdf;
using ImpiFact.Application.Features.Documents.Delivery;
using ImpiFact.Application.Features.Documents.Pdf;
using ImpiFact.Application.Tests.Behaviors;
using ImpiFact.Domain.Documents;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImpiFact.Application.Tests.Features.Documents.Pdf;

/// <summary>
/// Handler del mensaje <c>RenderPdf</c>: genera el PDF de un documento aceptado, lo guarda como archivo del documento
/// y encola <c>SendEmail</c> en la misma transacción.
/// </summary>
public sealed class RenderPdfHandlerTests
{
    private static readonly DocumentTotals Totals = new(9200000m, 9200000m, 10948000m, 0m, 0m, 0m, 10948000m);

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [Test]
    public async Task Stores_the_pdf_and_enqueues_the_email_in_one_save()
    {
        var scenario = new PdfScenario().WithGolden("invoice/withholdings-renta-iva-ica", 1, "SETP", 990000005, Totals);
        var (handler, renderer, journal) = Create(scenario);

        await handler.HandleAsync(Message(scenario.DocumentId), CancellationToken.None);

        var pdf = scenario.Db.Files.Single(file => file.Kind == DocumentFileKind.Pdf);
        Assert.Multiple(() =>
        {
            Assert.That(pdf.Content, Is.EqualTo(FakePdfRenderer.Bytes));
            Assert.That(pdf.ContentType, Is.EqualTo("application/pdf"));
            Assert.That(pdf.DocumentId, Is.EqualTo(scenario.DocumentId));
            Assert.That(pdf.CompanyId, Is.EqualTo(PdfScenario.CompanyId));
            Assert.That(renderer.Models.Single().Number, Is.EqualTo("990000005"));
            Assert.That(scenario.Db.Messages, Is.EqualTo(new[]
            {
                (OutboxMessageTypes.SendEmail, (object?)new SendEmailPayload(scenario.DocumentId), (Guid?)PdfScenario.CompanyId),
            }));
            Assert.That(journal.Entries, Is.EqualTo(new[] { "save" }));
        });
    }

    [Test]
    public async Task A_repeated_message_does_not_create_another_pdf_nor_another_email()
    {
        var scenario = new PdfScenario().WithGolden("invoice/withholdings-renta-iva-ica", 1, "SETP", 990000005, Totals);
        var (handler, renderer, journal) = Create(scenario);

        await handler.HandleAsync(Message(scenario.DocumentId), CancellationToken.None);
        await handler.HandleAsync(Message(scenario.DocumentId), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(scenario.Db.Files.Count(file => file.Kind == DocumentFileKind.Pdf), Is.EqualTo(1));
            Assert.That(scenario.Db.Messages, Has.Count.EqualTo(1));
            Assert.That(renderer.Models, Has.Count.EqualTo(1));
            Assert.That(journal.Entries, Is.EqualTo(new[] { "save" }));
        });
    }

    [TestCase(DocumentStatus.Queued)]
    [TestCase(DocumentStatus.Rejected)]
    [TestCase(DocumentStatus.Unconfirmed)]
    public void A_document_the_dian_did_not_accept_is_a_permanent_failure(DocumentStatus status)
    {
        var scenario = new PdfScenario().WithGolden("invoice/withholdings-renta-iva-ica", 1, "SETP", 990000005, Totals, status: status);
        var (handler, renderer, _) = Create(scenario);

        Assert.ThrowsAsync<PermanentOutboxFailureException>(() => handler.HandleAsync(Message(scenario.DocumentId), CancellationToken.None));
        Assert.That(renderer.Models, Is.Empty);
    }

    [Test]
    public async Task An_accepted_with_warnings_document_gets_its_pdf()
    {
        var scenario = new PdfScenario().WithGolden(
            "invoice/withholdings-renta-iva-ica", 1, "SETP", 990000005, Totals, status: DocumentStatus.AcceptedWithWarnings);
        var (handler, _, _) = Create(scenario);

        await handler.HandleAsync(Message(scenario.DocumentId), CancellationToken.None);

        Assert.That(scenario.Db.Files.Count(file => file.Kind == DocumentFileKind.Pdf), Is.EqualTo(1));
    }

    [Test]
    public void A_missing_document_is_a_permanent_failure()
    {
        var (handler, _, _) = Create(new PdfScenario());

        Assert.ThrowsAsync<PermanentOutboxFailureException>(() => handler.HandleAsync(Message(Guid.CreateVersion7()), CancellationToken.None));
    }

    [Test]
    public void A_document_without_its_stored_request_is_a_permanent_failure()
    {
        var scenario = new PdfScenario().WithGolden("invoice/withholdings-renta-iva-ica", 1, "SETP", 990000005, Totals);
        scenario.Db.Files.RemoveAll(file => file.Kind == DocumentFileKind.RequestJson);
        var (handler, _, _) = Create(scenario);

        Assert.ThrowsAsync<PermanentOutboxFailureException>(() => handler.HandleAsync(Message(scenario.DocumentId), CancellationToken.None));
        Assert.That(scenario.Db.Messages, Is.Empty);
    }

    [Test]
    public void Handles_the_render_pdf_messages()
    {
        var (handler, _, _) = Create(new PdfScenario());

        Assert.That(handler.MessageType, Is.EqualTo(OutboxMessageTypes.RenderPdf));
    }

    [TestCase(1, "FES-SETP990000005.pdf")]
    [TestCase(4, "NCS-SETP990000005.pdf")]
    [TestCase(5, "NDS-SETP990000005.pdf")]
    [TestCase(3, "FES-SETP990000005.pdf")]
    [TestCase(12, "FES-SETP990000005.pdf")]
    [TestCase(11, "DSS-SETP990000005.pdf")]
    [TestCase(13, "NDSNS-SETP990000005.pdf")]
    public void The_pdf_file_name_follows_apidian(int documentTypeId, string expected)
    {
        Assert.That(PdfFileNames.For(documentTypeId, "SETP", 990000005), Is.EqualTo(expected));
    }

    private static (RenderPdfHandler Handler, FakePdfRenderer Renderer, Journal Journal) Create(PdfScenario scenario)
    {
        var renderer = new FakePdfRenderer();
        var journal = new Journal();
        var handler = new RenderPdfHandler(
            scenario.Builder(), renderer, scenario.Db, scenario.Db, scenario.Db, new FakeUnitOfWork(journal), NullLogger<RenderPdfHandler>.Instance);
        return (handler, renderer, journal);
    }

    private static OutboxMessageContext Message(Guid documentId) => new(
        Guid.CreateVersion7(),
        PdfScenario.CompanyId,
        OutboxMessageTypes.RenderPdf,
        JsonSerializer.SerializeToNode(new RenderPdfPayload(documentId), Json)!,
        1,
        Json);

    private sealed class FakePdfRenderer : IPdfRenderer
    {
        public static readonly byte[] Bytes = "%PDF-1.7 fake"u8.ToArray();

        public List<DocumentPdfModel> Models { get; } = [];

        public Task<byte[]> RenderAsync(DocumentPdfModel model, CancellationToken ct)
        {
            Models.Add(model);
            return Task.FromResult(Bytes);
        }
    }
}
