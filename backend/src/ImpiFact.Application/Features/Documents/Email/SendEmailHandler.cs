using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Email;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Application.Features.Documents.Delivery;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Features.Documents.Email;

/// <summary>
/// El AttachedDocument (contenedor electrónico) o el PDF del documento todavía no están guardados. Es una falla
/// transitoria: el Worker reintenta el mensaje con retroceso mientras el PDF se genera.
/// </summary>
public sealed class EmailAttachmentsNotReadyException(string message) : Exception(message);

/// <summary>
/// Handler del mensaje <c>SendEmail</c> en el Worker (Arquitectura detallada, sección 7, paso 9; supuesto C6): envía al
/// comprador el correo del documento aceptado con un ZIP que lleva el AttachedDocument y el PDF, como ApiDian (la
/// implementación de referencia en PHP), y anota el resultado en <c>email_status</c> y <c>email_sent_at</c>.
/// </summary>
/// <remarks>
/// <para>
/// Solo envía si la solicitud guardada lo pidió (<c>sendmail</c>) y con los destinatarios de
/// <see cref="DocumentEmailRecipients"/>; sale con el servidor de la empresa o el de la plataforma
/// (<see cref="MailServerSelector"/>). Si falta el PDF o el AttachedDocument lanza
/// <see cref="EmailAttachmentsNotReadyException"/> para que el Worker reintente; si el Worker se rinde,
/// <see cref="OnFailedAsync"/> deja <c>email_status</c> en fallido.
/// </para>
/// <para>
/// Es idempotente: con <c>email_status</c> enviado no vuelve a enviar. Queda una ventana pequeña: si el servidor
/// recibió el correo y el guardado posterior falla, el reintento lo envía otra vez (entrega «al menos una vez»).
/// Los logs nunca llevan direcciones de correo ni la contraseña.
/// </para>
/// </remarks>
internal sealed class SendEmailHandler(
    IDocumentDelivery documents,
    IDocumentStore store,
    IDocumentFileReads files,
    IIssuerContextReads issuers,
    IDocumentCatalogs catalogs,
    IDianQrUrls qrUrls,
    MailServerSelector servers,
    IEmailSender sender,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<SendEmailHandler> logger) : IOutboxMessageHandler
{
    public string MessageType => OutboxMessageTypes.SendEmail;

    public async Task HandleAsync(OutboxMessageContext message, CancellationToken ct)
    {
        var payload = message.GetPayload<SendEmailPayload>();
        var companyId = message.CompanyId
            ?? throw new PermanentOutboxFailureException($"Outbox message {message.MessageId} has no company.");
        var document = await documents.FindForUpdateAsync(companyId, payload.DocumentId, ct)
            ?? throw new PermanentOutboxFailureException($"Document {payload.DocumentId} does not exist.");
        if (document.EmailStatus == EmailStatus.Sent)
        {
            logger.LogInformation("The email of document {DocumentId} was already sent; it is not sent again.", document.Id);
            return;
        }

        if (document.Status is not (DocumentStatus.Accepted or DocumentStatus.AcceptedWithWarnings))
        {
            logger.LogInformation("Document {DocumentId} is {Status}; only accepted documents are emailed.", document.Id, document.Status);
            return;
        }

        var request = DocumentEmailRequest.Parse(await store.ReadFileAsync(companyId, document.Id, DocumentFileKind.RequestJson, ct));
        var issuer = await issuers.GetAsync(companyId, document.DocumentTypeId, DocumentFamilies.Of(document.DocumentTypeId), ct)
            ?? throw new PermanentOutboxFailureException($"Company {companyId} does not exist.");
        var recipients = DocumentEmailRecipients.Resolve(
            request, document.CustomerSnapshot.IdentificationNumber, document.CustomerSnapshot.Email, issuer.Company.Email);
        if (recipients.Skip is { } reason)
        {
            logger.LogInformation("Document {DocumentId} gets no email: {Reason}.", document.Id, reason);
            if (reason == EmailSkipReason.NoRecipient)
            {
                document.EmailStatus = EmailStatus.Failed;
                await unitOfWork.SaveChangesAsync(ct);
            }

            return;
        }

        var attachedDocument = await files.ReadLatestAsync(companyId, document.Id, DocumentFileKind.AttachedDocument, ct)
            ?? throw new EmailAttachmentsNotReadyException($"Document {document.Id} has no attached document yet.");
        var pdf = await files.ReadLatestAsync(companyId, document.Id, DocumentFileKind.Pdf, ct)
            ?? throw new EmailAttachmentsNotReadyException($"Document {document.Id} has no PDF yet.");
        var sentZip = await files.ReadLatestAsync(companyId, document.Id, DocumentFileKind.SentZip, ct);

        var typeKey = new CatalogKey(DianCatalog.DocumentType, document.DocumentTypeId);
        var typeCode = (await catalogs.FindAsync([typeKey], ct)).TryGetValue(typeKey, out var type)
            ? type.Code
            : throw new PermanentOutboxFailureException($"Document type {document.DocumentTypeId} does not exist.");
        var company = issuer.Company;
        var name = DocumentEmailComposer.AttachedDocumentName(request.AttachedDocumentNamePrefix, sentZip, document.FullNumber);
        var (html, text) = DocumentEmailComposer.Body(
            new DocumentEmailContent(
                company.Name,
                document.CustomerSnapshot.Name,
                document.CustomerSnapshot.IdentificationNumber,
                document.Prefix,
                document.Number,
                document.FullNumber,
                document.IssueDate,
                document.IssueTime,
                document.PayableAmount,
                qrUrls.ForEnvironment(document.TypeEnvironmentId) + document.Cufe),
            request);
        var email = new EmailMessage(
            recipients.To,
            recipients.Bcc,
            DocumentEmailComposer.Subject(company.IdentificationNumber, company.Name, document.FullNumber, typeCode),
            html,
            text,
            [DocumentEmailComposer.Attachment(name, attachedDocument, pdf)]);

        var server = await servers.SelectAsync(companyId, ct);
        await sender.SendAsync(server, email, ct);
        document.EmailStatus = EmailStatus.Sent;
        document.EmailSentAt = clock.UtcNow;
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation(
            "The email of document {DocumentId} was sent to {RecipientCount} recipients through {Server}.",
            document.Id, recipients.To.Count + recipients.Bcc.Count, server);
    }

    /// <summary>
    /// El Worker se rindió con el correo (por ejemplo, el PDF nunca llegó o el servidor de correo no respondió en
    /// ningún intento): si no se envió, <c>email_status</c> queda fallido. El aviso de la tarea fallida lo deja el Worker.
    /// </summary>
    public async Task OnFailedAsync(OutboxMessageContext message, CancellationToken ct)
    {
        var payload = message.GetPayload<SendEmailPayload>();
        if (message.CompanyId is not { } companyId
            || await documents.FindForUpdateAsync(companyId, payload.DocumentId, ct) is not { } document
            || document.EmailStatus == EmailStatus.Sent)
        {
            return;
        }

        document.EmailStatus = EmailStatus.Failed;
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogWarning("The email of document {DocumentId} could not be sent after every attempt; it is marked Failed.", document.Id);
    }
}
