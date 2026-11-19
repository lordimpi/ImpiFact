using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Abstractions.Observability;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Dian.Soap;
using ImpiFact.Domain.Catalog;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Features.Documents.Delivery;

/// <summary>
/// Handler del mensaje <c>SendDocumentToDian</c> en el Worker (Arquitectura detallada, sección 7, pasos 7, 8 y 10):
/// empaqueta el XML firmado en el ZIP (archivo comprimido) con el nombre que exige la DIAN (la autoridad tributaria de
/// Colombia), lo envía y deja el documento en el estado que corresponde a la respuesta.
/// </summary>
/// <remarks>
/// <para>
/// Operación: en producción, SendBillSync; en habilitación (pruebas) con <c>testSetId</c> (el set de pruebas que
/// asigna la DIAN, de la ruta <c>/{testSetId}</c>), SendTestSetAsync; en habilitación sin él, SendBillSync al servicio
/// de habilitación, como ApiDian (la implementación de referencia en PHP).
/// </para>
/// <para>
/// Estados (supuesto C4): si no se alcanzó a enviar, el documento sigue en <c>Queued</c> y el mensaje se reintenta; si
/// la DIAN decidió, <c>Sent</c> y luego <c>Accepted</c>, <c>AcceptedWithWarnings</c> o <c>Rejected</c>; con la regla
/// 90 se consulta GetStatus con el CUFE (código único de la factura electrónica) y se toma esa respuesta; si se envió
/// sin respuesta o «en proceso» (98), <c>Sent</c> → <c>Unconfirmed</c> y se programa <c>PollDianStatus</c>; con la
/// ZipKey del set de pruebas, <c>Sent</c> y se programa GetStatusZip; con un SOAP Fault del que envía,
/// <c>Failed</c> y aviso, salvo que un intento anterior (de esta llamada o de un mensaje anterior) haya podido llegar
/// a la DIAN: entonces cuenta como «sin respuesta». Un documento que ya no está en <c>Queued</c> no se vuelve a
/// enviar: el mensaje repetido no hace nada.
/// </para>
/// </remarks>
internal sealed class SendDocumentToDianHandler(
    DianDelivery delivery,
    IDocumentDelivery documents,
    IDocumentStore store,
    IIssuerContextReads issuers,
    ICertificateStore certificates,
    IDianServiceUrls urls,
    INumberingSequences numbering,
    IUnitOfWork unitOfWork,
    IClock clock,
    ImpiFactMetrics metrics,
    ILogger<SendDocumentToDianHandler> logger) : IOutboxMessageHandler
{
    /// <summary>Espera hasta la primera consulta de un documento enviado sin decisión.</summary>
    public static readonly TimeSpan FirstStatusPoll = TimeSpan.FromMinutes(1);

    public string MessageType => OutboxMessageTypes.SendDocumentToDian;

    public async Task HandleAsync(OutboxMessageContext message, CancellationToken ct)
    {
        var payload = message.GetPayload<SendDocumentToDianPayload>();
        var companyId = message.CompanyId
            ?? throw new PermanentOutboxFailureException($"Outbox message {message.MessageId} has no company.");
        var document = await documents.FindForUpdateAsync(companyId, payload.DocumentId, ct)
            ?? throw new PermanentOutboxFailureException($"Document {payload.DocumentId} does not exist.");
        if (document.Status != DocumentStatus.Queued)
        {
            logger.LogInformation("Document {DocumentId} is already {Status}; it is not sent again.", document.Id, document.Status);
            return;
        }

        var kind = FileKindOf(document);
        var signedXml = await store.ReadFileAsync(companyId, document.Id, DocumentFileKind.SignedXml, ct)
            ?? throw new PermanentOutboxFailureException($"Document {document.Id} has no signed XML.");
        var issuer = await issuers.GetAsync(companyId, document.DocumentTypeId, DocumentKind.Invoice, ct)
            ?? throw new PermanentOutboxFailureException($"Company {companyId} does not exist.");
        var secretRef = issuer.Certificate?.SecretRef
            ?? throw new PermanentOutboxFailureException($"Company {companyId} has no active certificate.");

        var testSetId = document.TypeEnvironmentId == TypeEnvironment.Production || string.IsNullOrWhiteSpace(payload.TestSetId)
            ? null
            : payload.TestSetId.Trim();
        var operation = testSetId is null ? DianOperation.SendBillSync : DianOperation.SendTestSetAsync;

        // El certificado y la dirección se resuelven antes de guardar la fila del intento: así una fila sin resultado
        // (finished_at nulo) siempre es una llamada que empezó y que la DIAN pudo haber recibido.
        using var certificate = await certificates.LoadAsync(companyId, secretRef, ct);
        var endpoint = new DianEndpoint(urls.ForEnvironment(document.TypeEnvironmentId), certificate);
        var startedAt = clock.UtcNow;

        // Transacción corta: el consecutivo de nombres de archivo y la fila del intento con el ZIP enviado.
        DianSubmission submission;
        byte[] zip;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(ct))
        {
            var year = ColombiaTime.Today(startedAt).Year;
            var counter = await TakeFileCounterAsync(companyId, kind, year, ct);
            var entry = DianFileNames.XmlEntry(kind, issuer.Company.IdentificationNumber, year, counter);
            zip = DianZip.Create(entry, signedXml, ColombiaTime.Local(startedAt));
            submission = await delivery.BeginAsync(document, operation, zipKey: null, zip, ct);
            await transaction.CommitAsync(ct);
        }

        var fileName = DianFileNames.SoapFileName(document.Prefix, document.Number);
        var content = Convert.ToBase64String(zip);
        DianSoapRequest request = testSetId is null
            ? new SendBillSyncRequest(fileName, content)
            : new SendTestSetAsyncRequest(fileName, content, testSetId);

        var answer = await delivery.CallAsync(submission, request, endpoint, ct);
        if (answer is DianAnswer.AlreadyProcessed)
        {
            answer = await delivery.ReconcileAsync(document, endpoint, ct);
        }

        if (answer is DianAnswer.Refused refused
            && await documents.FirstPossibleArrivalAsync(companyId, document.Id, submission.Id, ct) is not null)
        {
            // Un intento anterior pudo llegar a la DIAN (por ejemplo, su respuesta no se alcanzó a guardar): el SOAP
            // Fault puede ser la DIAN rechazando una repetición, no el documento. Se consulta su estado.
            answer = new DianAnswer.NoAnswer($"An earlier attempt may have reached the DIAN; this one was refused: {refused.Reason}");
        }

        if (answer is DianAnswer.NotDelivered notDelivered)
        {
            // Se guarda el intento con su resultado; el documento sigue en Queued y el Worker reintenta el mensaje.
            await unitOfWork.SaveChangesAsync(ct);
            throw new DianNotDeliveredException($"The DIAN did not receive document {document.Id}: {notDelivered.Reason}");
        }

        await ApplyAsync(document, answer, startedAt, endpoint, ct);
        await unitOfWork.SaveChangesAsync(ct);

        // Después de guardar: un guardado que falla (por ejemplo, otro proceso ya lo cambió) no cuenta.
        metrics.DocumentIssued(document);
        metrics.DocumentDecided(document);
    }

    /// <summary>
    /// El Worker se rindió con el mensaje (por ejemplo, la DIAN no estuvo disponible en ninguno de los intentos) y el
    /// documento sigue en <c>Queued</c>. Si algún intento guardado pudo llegar a la DIAN (ver
    /// <see cref="IDocumentDelivery.FirstPossibleArrivalAsync"/>), el documento pasa a <c>Sent</c> → <c>Unconfirmed</c>
    /// y se programa <c>PollDianStatus</c> con GetStatus y el CUFE, porque la DIAN pudo haberlo aceptado; solo si
    /// ninguno pudo llegar pasa a <c>Failed</c>. El aviso de la tarea fallida lo deja el Worker.
    /// </summary>
    /// <remarks>
    /// El Worker lo llama antes de guardar el fallo del mensaje y puede llamarlo otra vez si el proceso se cae en medio:
    /// un documento que ya no está en <c>Queued</c> no se toca.
    /// </remarks>
    public async Task OnFailedAsync(OutboxMessageContext message, CancellationToken ct)
    {
        var payload = message.GetPayload<SendDocumentToDianPayload>();
        if (message.CompanyId is not { } companyId
            || await documents.FindForUpdateAsync(companyId, payload.DocumentId, ct) is not { Status: DocumentStatus.Queued } document)
        {
            return;
        }

        if (await documents.FirstPossibleArrivalAsync(companyId, document.Id, exceptSubmissionId: null, ct) is { } sentAt)
        {
            document.MoveTo(DocumentStatus.Sent);
            document.SentAt = sentAt;
            document.MoveTo(DocumentStatus.Unconfirmed);
            await delivery.SchedulePollAsync(document, DianStatusQuery.GetStatus, clock.UtcNow + FirstStatusPoll, ct);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogWarning(
                "Document {DocumentId} could not be sent to the DIAN after every attempt, but an attempt may have reached it; it is marked Unconfirmed and its status will be polled.",
                document.Id);
            return;
        }

        document.MoveTo(DocumentStatus.Failed);
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogWarning("Document {DocumentId} could not be sent to the DIAN after every attempt; it is marked Failed.", document.Id);
    }

    private async Task ApplyAsync(Document document, DianAnswer answer, DateTimeOffset startedAt, DianEndpoint endpoint, CancellationToken ct)
    {
        if (answer is DianAnswer.Refused refused)
        {
            delivery.Refuse(document, refused.Reason);
            return;
        }

        document.MoveTo(DocumentStatus.Sent);
        document.SentAt = startedAt;
        switch (answer)
        {
            case DianAnswer.Decided decided:
                await delivery.DecideAsync(document, decided, endpoint, ct);
                break;
            case DianAnswer.ZipReceived received:
                document.TrackId = received.ZipKey;
                await delivery.SchedulePollAsync(document, DianStatusQuery.GetStatusZip, clock.UtcNow + DianPollSchedule.FirstZipPoll, ct);
                break;
            default:
                // Sin respuesta, «en proceso» (98) o un resultado que no decide: se consulta después con el CUFE.
                DianDelivery.Stamp(document, (answer as DianAnswer.InProcess)?.Result);
                document.MoveTo(DocumentStatus.Unconfirmed);
                await delivery.SchedulePollAsync(document, DianStatusQuery.GetStatus, clock.UtcNow + FirstStatusPoll, ct);
                break;
        }
    }

    /// <summary>
    /// Siguiente número del consecutivo de nombres de archivo de la empresa para ese tipo y año (supuesto C2). El
    /// primer envío del año crea el consecutivo en 1; si dos envíos lo crean a la vez, uno choca con el índice único y
    /// su mensaje se reintenta.
    /// </summary>
    private async Task<long> TakeFileCounterAsync(Guid companyId, DianFileKind kind, int year, CancellationToken ct)
    {
        var scopeKey = NumberingScope.ForDianFile(FilePrefixOf(kind), year);
        if (await numbering.TakeNextAsync(companyId, scopeKey, ct) is { } taken)
        {
            return taken;
        }

        await numbering.OpenAsync(companyId, scopeKey, firstNumber: 1, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return await numbering.TakeNextAsync(companyId, scopeKey, ct)
            ?? throw new InvalidOperationException($"The file name sequence {scopeKey} was not created.");
    }

    private static DianFileKind FileKindOf(Document document) => document.DocumentTypeId switch
    {
        // Factura de venta nacional, de exportación y de contingencia.
        1 or 2 or 3 => DianFileKind.Invoice,
        4 => DianFileKind.CreditNote,
        5 => DianFileKind.DebitNote,
        _ => throw new PermanentOutboxFailureException(
            $"Document {document.Id} has type {document.DocumentTypeId}, which is not sent to the DIAN yet."),
    };

    private static string FilePrefixOf(DianFileKind kind) => kind switch
    {
        DianFileKind.Invoice => "fv",
        DianFileKind.CreditNote => "nc",
        _ => "nd",
    };
}
