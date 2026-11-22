using System.Text.Json;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Abstractions.Pdf;
using ImpiFact.Application.Features.Documents.Maintenance;
using ImpiFact.Application.Tests.Behaviors;
using ImpiFact.Application.Tests.Features.Documents.Pdf;
using ImpiFact.Domain.Documents;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImpiFact.Application.Tests.Features.Documents.Maintenance;

/// <summary>
/// Handler del mensaje <c>RegeneratePdf</c> (ruta <c>regeneratepdf</c>): genera otra revisión del PDF de un documento
/// aceptado con el mismo armado y el mismo renderizador de <c>RenderPdf</c>, sin encolar el correo.
/// </summary>
public sealed class RegeneratePdfOutboxHandlerTests
{
    private static readonly DocumentTotals Totals = new(9200000m, 9200000m, 10948000m, 0m, 0m, 0m, 10948000m);

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static readonly byte[] Rendered = "%PDF-1.7 regenerated"u8.ToArray();

    [Test]
    public async Task Stores_the_next_revision_of_the_pdf_and_queues_no_email()
    {
        var scenario = new PdfScenario().WithGolden("invoice/withholdings-renta-iva-ica", 1, "SETP", 990000005, Totals);
        scenario.Db.Files.Add(new NewDocumentFile(PdfScenario.CompanyId, scenario.DocumentId, DocumentFileKind.Pdf, [1, 2], "application/pdf"));
        var (handler, renderer, journal) = Create(scenario, DocumentStatus.Accepted);

        await handler.HandleAsync(Message(scenario.DocumentId), CancellationToken.None);

        var pdfs = scenario.Db.Files.Where(file => file.Kind == DocumentFileKind.Pdf).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(pdfs.Select(file => file.Revision), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(pdfs[1].Content, Is.EqualTo(Rendered));
            Assert.That(pdfs[1].ContentType, Is.EqualTo("application/pdf"));
            Assert.That(renderer.Models.Single().Number, Is.EqualTo("990000005"));
            Assert.That(scenario.Db.Messages, Is.Empty);
            Assert.That(journal.Entries, Is.EqualTo(new[] { "save" }));
        });
    }

    [Test]
    public async Task A_document_without_a_pdf_gets_its_first_revision()
    {
        var scenario = new PdfScenario().WithGolden("invoice/withholdings-renta-iva-ica", 1, "SETP", 990000005, Totals);
        var (handler, _, _) = Create(scenario, DocumentStatus.AcceptedWithWarnings);

        await handler.HandleAsync(Message(scenario.DocumentId), CancellationToken.None);

        Assert.That(scenario.Db.Files.Single(file => file.Kind == DocumentFileKind.Pdf).Revision, Is.EqualTo(1));
    }

    [TestCase(DocumentStatus.Queued)]
    [TestCase(DocumentStatus.Rejected)]
    [TestCase(DocumentStatus.Failed)]
    public void A_document_the_dian_did_not_accept_is_a_permanent_failure(DocumentStatus status)
    {
        var scenario = new PdfScenario().WithGolden("invoice/withholdings-renta-iva-ica", 1, "SETP", 990000005, Totals, status: status);
        var (handler, renderer, _) = Create(scenario, status);

        Assert.ThrowsAsync<PermanentOutboxFailureException>(() => handler.HandleAsync(Message(scenario.DocumentId), CancellationToken.None));
        Assert.That(renderer.Models, Is.Empty);
    }

    [Test]
    public void A_document_without_its_stored_request_is_a_permanent_failure()
    {
        var scenario = new PdfScenario().WithGolden("invoice/withholdings-renta-iva-ica", 1, "SETP", 990000005, Totals);
        scenario.Db.Files.RemoveAll(file => file.Kind == DocumentFileKind.RequestJson);
        var (handler, _, journal) = Create(scenario, DocumentStatus.Accepted);

        Assert.ThrowsAsync<PermanentOutboxFailureException>(() => handler.HandleAsync(Message(scenario.DocumentId), CancellationToken.None));
        Assert.That(journal.Entries, Is.Empty);
    }

    [Test]
    public void Handles_the_regenerate_pdf_messages()
    {
        var (handler, _, _) = Create(new PdfScenario(), DocumentStatus.Accepted);

        Assert.That(handler.MessageType, Is.EqualTo(OutboxMessageTypes.RegeneratePdf));
    }

    private static (RegeneratePdfOutboxHandler Handler, FakeRenderer Renderer, Journal Journal) Create(PdfScenario scenario, DocumentStatus status)
    {
        var renderer = new FakeRenderer();
        var journal = new Journal();
        var handler = new RegeneratePdfOutboxHandler(
            scenario.Builder(), renderer, new ScenarioLookups(scenario, status), scenario.Db, new FakeUnitOfWork(journal),
            NullLogger<RegeneratePdfOutboxHandler>.Instance);
        return (handler, renderer, journal);
    }

    private static OutboxMessageContext Message(Guid documentId) => new(
        Guid.CreateVersion7(),
        PdfScenario.CompanyId,
        OutboxMessageTypes.RegeneratePdf,
        JsonSerializer.SerializeToNode(new RegeneratePdfPayload(documentId), Json)!,
        1,
        Json);

    private sealed class FakeRenderer : IPdfRenderer
    {
        public List<DocumentPdfModel> Models { get; } = [];

        public Task<byte[]> RenderAsync(DocumentPdfModel model, CancellationToken ct)
        {
            Models.Add(model);
            return Task.FromResult(Rendered);
        }
    }

    /// <summary>Búsquedas sobre el documento del escenario, con el estado indicado.</summary>
    private sealed class ScenarioLookups(PdfScenario scenario, DocumentStatus status) : IDocumentLookups
    {
        public Task<DocumentRef?> FindByIdAsync(Guid companyId, Guid documentId, CancellationToken ct) =>
            Task.FromResult(companyId == PdfScenario.CompanyId && documentId == scenario.DocumentId
                ? new DocumentRef(documentId, 1, 2, "SETP", 990000005, "SETP990000005", status, "cufe")
                : null);

        public Task<int> LatestFileRevisionAsync(Guid companyId, Guid documentId, DocumentFileKind kind, CancellationToken ct) =>
            Task.FromResult(scenario.Db.Files.Where(file => file.CompanyId == companyId && file.DocumentId == documentId && file.Kind == kind)
                .Select(file => file.Revision).DefaultIfEmpty(0).Max());

        public Task<DocumentRef?> FindByCufeAsync(Guid companyId, string cufe, CancellationToken ct) => throw new NotSupportedException();

        public Task<byte[]?> LatestDianAnswerAsync(Guid companyId, Guid documentId, CancellationToken ct) => throw new NotSupportedException();

        public Task<ZipAnswer?> FindZipAnswerAsync(Guid companyId, string zipKey, CancellationToken ct) => throw new NotSupportedException();
    }
}
