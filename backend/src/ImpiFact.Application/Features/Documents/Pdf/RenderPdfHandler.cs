using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Abstractions.Pdf;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Application.Features.Documents.Delivery;
using ImpiFact.Domain.Documents;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Features.Documents.Pdf;

/// <summary>
/// Handler del mensaje <c>RenderPdf</c> en el Worker (Arquitectura detallada, sección 7, paso 9): genera la
/// representación gráfica (el PDF) de un documento que la DIAN (la autoridad tributaria de Colombia) ya aceptó, la
/// guarda como archivo del documento y encola <c>SendEmail</c>. El archivo y el mensaje se guardan juntos, en el mismo
/// <c>SaveChanges</c>: o quedan los dos o ninguno.
/// </summary>
/// <remarks>
/// Es idempotente: si el documento ya tiene su PDF, el mensaje repetido no hace nada, porque el PDF y su
/// <c>SendEmail</c> se guardaron juntos. Un documento que no existe, que la DIAN no aceptó o al que le falta lo que el
/// PDF necesita es una falla permanente: reintentar no lo arregla.
/// </remarks>
internal sealed class RenderPdfHandler(
    DocumentPdfModelBuilder builder,
    IPdfRenderer renderer,
    IDocumentStore store,
    IDocumentFiles files,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    ILogger<RenderPdfHandler> logger) : IOutboxMessageHandler
{
    public string MessageType => OutboxMessageTypes.RenderPdf;

    public async Task HandleAsync(OutboxMessageContext message, CancellationToken ct)
    {
        var payload = message.GetPayload<RenderPdfPayload>();
        var companyId = message.CompanyId
            ?? throw new PermanentOutboxFailureException($"Outbox message {message.MessageId} has no company.");
        var document = await store.FindByIdAsync(companyId, payload.DocumentId, ct)
            ?? throw new PermanentOutboxFailureException($"Document {payload.DocumentId} does not exist.");
        if (document.Status is not (DocumentStatus.Accepted or DocumentStatus.AcceptedWithWarnings))
        {
            throw new PermanentOutboxFailureException($"Document {document.Id} is {document.Status}; only accepted documents get a PDF.");
        }

        if (await store.ReadFileAsync(companyId, document.Id, DocumentFileKind.Pdf, ct) is not null)
        {
            logger.LogInformation("Document {DocumentId} already has its PDF; it is not rendered again.", document.Id);
            return;
        }

        DocumentPdfModel model;
        try
        {
            model = await builder.BuildAsync(companyId, document.Id, ct)
                ?? throw new PermanentOutboxFailureException($"Document {document.Id} does not exist.");
        }
        catch (PdfSourceException exception)
        {
            throw new PermanentOutboxFailureException(exception.Message, exception);
        }

        var pdf = await renderer.RenderAsync(model, ct);
        await files.AddAsync(new NewDocumentFile(companyId, document.Id, DocumentFileKind.Pdf, pdf, "application/pdf"), ct);
        await outbox.EnqueueAsync(OutboxMessageTypes.SendEmail, new SendEmailPayload(document.Id), companyId, notBefore: null, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
