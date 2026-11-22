using System.Text;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Dian;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Abstractions.Notifications;
using ImpiFact.Application.Abstractions.Observability;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Application.Features.Documents.Delivery;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Dian.Soap;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Features.Documents.Batches;

/// <summary>Notice types of the batches (<c>notifications.type</c>).</summary>
public static class BatchNotificationTypes
{
    /// <summary>The ZIP of a batch never reached the DIAN, or the DIAN refused the message: its documents failed.</summary>
    public const string Failed = "DocumentBatchFailed";
}

/// <summary>
/// Pieces shared by the two Worker handlers of a batch (phase 7a, T8): the DIAN call recorded in the attempt of each document
/// (<c>dian_submissions</c>), the verdict of each document with the same rules and side effects as a document sent alone
/// (<see cref="DianDelivery.DecideAsync"/>: plan quota, AttachedDocument, PDF and email when accepted; notice when
/// rejected) and the fall-back to the individual GetStatus of each document when the DIAN may have received the ZIP without
/// giving its ZipKey.
/// </summary>
internal sealed class BatchDelivery(
    DianDelivery delivery,
    IDianWebService dian,
    IDocumentBatches batches,
    IDocumentDelivery documents,
    IDocumentFiles files,
    IIssuerContextReads issuers,
    ICertificateStore certificates,
    IDianServiceUrls urls,
    IOutbox outbox,
    ICompanyNotifications notifications,
    IClock clock,
    ILogger<BatchDelivery> logger)
{
    private const int TrackIdMaxLength = 64;
    private const int StatusCodeMaxLength = 10;
    private const int StatusDescriptionMaxLength = 500;
    private static readonly UTF8Encoding Utf8 = new(false);

    public IClock Clock => clock;

    /// <summary>The issuer of the batch (the invoice family: every batched type is an invoice or a note).</summary>
    public Task<IssuerContext?> IssuerAsync(Guid companyId, CancellationToken ct) => issuers.GetAsync(companyId, 1, DocumentKind.Invoice, ct);

    public async Task<DianEndpoint> EndpointAsync(Guid companyId, string secretRef, int typeEnvironmentId, CancellationToken ct) =>
        new(urls.ForEnvironment(typeEnvironmentId), await certificates.LoadAsync(companyId, secretRef, ct));

    public Task<DianCall> CallAsync(DianSoapRequest request, DianEndpoint endpoint, CancellationToken ct) =>
        dian.CallAsync(request, endpoint.Url, endpoint.Certificate, ct);

    /// <summary>A started attempt of the document in the batch call (finished later by <see cref="Finish"/>).</summary>
    public async Task<DianSubmission> BeginAsync(Document document, DianOperation operation, string? zipKey, CancellationToken ct)
    {
        var submission = new DianSubmission
        {
            Id = Guid.CreateVersion7(),
            CompanyId = document.CompanyId,
            DocumentId = document.Id,
            Attempt = await documents.NextSubmissionAttemptAsync(document.CompanyId, document.Id, ct),
            Operation = operation,
            TypeEnvironmentId = document.TypeEnvironmentId,
            TrackId = Truncate(zipKey, TrackIdMaxLength),
            ZipKey = Truncate(zipKey, TrackIdMaxLength),
            StartedAt = clock.UtcNow,
        };
        documents.AddSubmission(submission);
        return submission;
    }

    /// <summary>Writes in the attempt what the call answered: HTTP status, duration and, when it brings one, the result.</summary>
    public void Finish(DianSubmission submission, DianCall call, DianResult? result)
    {
        submission.FinishedAt = clock.UtcNow;
        submission.DurationMs = (int)Math.Min(int.MaxValue, call.Duration.TotalMilliseconds);
        submission.HttpStatus = call.HttpStatus;
        if (result is not null)
        {
            submission.IsValid = result.IsValid;
            submission.DianStatusCode = Truncate(result.StatusCode, StatusCodeMaxLength);
            submission.DianStatusDescription = Truncate(result.StatusDescription, StatusDescriptionMaxLength);
            submission.ErrorMessages = result.ErrorMessages.Count > 0 ? [.. result.ErrorMessages] : null;
            if (!string.IsNullOrWhiteSpace(result.ZipKey))
            {
                submission.ZipKey = Truncate(result.ZipKey, TrackIdMaxLength);
                submission.TrackId = submission.ZipKey;
            }
        }
        else if (call.Response is DianSoapFaultResponse fault)
        {
            submission.DianStatusDescription = Truncate(fault.Reason, StatusDescriptionMaxLength);
        }
    }

    /// <summary>Stores the ApplicationResponse of a document's verdict next to its attempt.</summary>
    public async Task KeepApplicationResponseAsync(DianSubmission submission, DianResult result, CancellationToken ct)
    {
        if (result.XmlBase64Bytes is { Length: > 0 } applicationResponse)
        {
            await files.AddAsync(
                new NewDocumentFile(submission.CompanyId, submission.DocumentId, DocumentFileKind.ApplicationResponse, applicationResponse,
                    "application/xml", submission.Attempt), ct);
        }
    }

    /// <summary>Stores the SOAP body the DIAN answered to the batch call and returns its key; null without a body.</summary>
    public async Task<string?> KeepBodyAsync(DocumentBatch batch, DianCall call, CancellationToken ct) =>
        call.Body is null ? null : await batches.PutFileAsync(batch.CompanyId, batch.Id, Utf8.GetBytes(call.Body), "application/soap+xml", ".xml", ct);

    /// <summary>The DIAN decided one document of the batch: the same path as a document sent alone.</summary>
    public Task DecideAsync(Document document, DianAnswer.Decided decided, DianEndpoint endpoint, CancellationToken ct) =>
        delivery.DecideAsync(document, decided, endpoint, ct);

    /// <summary>Rule 90 for a document of the batch: its status is asked with GetStatus and its code.</summary>
    public Task<DianAnswer> ReconcileAsync(Document document, DianEndpoint endpoint, CancellationToken ct) =>
        delivery.ReconcileAsync(document, endpoint, ct);

    /// <summary>
    /// The DIAN may have received the ZIP but gave no ZipKey to follow it: each document goes Sent → Unconfirmed and is asked
    /// on its own with GetStatus and its code, like a document sent alone without an answer. Nothing is sent again.
    /// </summary>
    /// <param name="completeBatch">False when only some documents of a batch still followed by GetStatusZip go on their own.</param>
    public async Task FollowOneByOneAsync(
        DocumentBatch batch, IReadOnlyList<Document> pending, DateTimeOffset sentAt, CancellationToken ct, bool completeBatch = true)
    {
        foreach (var document in pending)
        {
            if (document.Status == DocumentStatus.Queued)
            {
                document.MoveTo(DocumentStatus.Sent);
                document.SentAt = sentAt;
            }

            if (document.Status == DocumentStatus.Sent)
            {
                document.MoveTo(DocumentStatus.Unconfirmed);
            }

            await delivery.SchedulePollAsync(document, DianStatusQuery.GetStatus, clock.UtcNow + SendDocumentToDianHandler.FirstStatusPoll, ct);
        }

        if (!completeBatch)
        {
            return;
        }

        batch.DianStatusDescription ??= "La DIAN no devolvió la ZipKey del lote: el estado de cada documento se consulta por su código.";
        batch.Complete(clock.UtcNow, sentAt);
        logger.LogWarning("Batch {BatchId} may have reached the DIAN without a ZipKey; its {Count} documents are followed one by one.", batch.Id, pending.Count);
    }

    /// <summary>The ZIP never reached the DIAN (or the DIAN refused the message): the batch and its documents fail, with one notice.</summary>
    public void Fail(DocumentBatch batch, IReadOnlyList<Document> pending, string? reason)
    {
        foreach (var document in pending.Where(document => document.Status == DocumentStatus.Queued))
        {
            document.MoveTo(DocumentStatus.Failed);
            document.DianStatusDescription = Truncate(reason, StatusDescriptionMaxLength) ?? document.DianStatusDescription;
        }

        batch.DianStatusDescription = Truncate(reason, StatusDescriptionMaxLength) ?? batch.DianStatusDescription;
        batch.Fail(clock.UtcNow);
        notifications.Add(
            batch.CompanyId,
            BatchNotificationTypes.Failed,
            "Un lote no llegó a la DIAN",
            $"El lote {batch.Name} no llegó a la DIAN y sus {pending.Count} documentos quedaron como fallidos. Revise el certificado y el " +
            "software configurados y emita los documentos de nuevo.");
        logger.LogWarning("Batch {BatchId} did not reach the DIAN ({Reason}); its documents are marked Failed.", batch.Id, reason);
    }

    /// <summary>Schedules the next GetStatusZip of the batch.</summary>
    public Task SchedulePollAsync(DocumentBatch batch, DateTimeOffset notBefore, CancellationToken ct) =>
        outbox.EnqueueAsync(OutboxMessageTypes.PollDocumentBatchStatus, new DocumentBatchPayload(batch.Id), batch.CompanyId, notBefore, ct);

    /// <summary>
    /// The DIAN gave no verdict yet: the next GetStatusZip follows <see cref="DianPollSchedule"/> from the send; after its 24
    /// hours each document still without verdict fails with the notice of an unconfirmed document.
    /// </summary>
    public async Task ContinueAsync(DocumentBatch batch, IReadOnlyList<Document> pending, CancellationToken ct)
    {
        var now = clock.UtcNow;
        if (DianPollSchedule.Next(batch.SentAt ?? now, now) is { } next)
        {
            await SchedulePollAsync(batch, next, ct);
            return;
        }

        foreach (var document in pending)
        {
            delivery.GiveUp(document);
        }

        batch.Complete(now);
    }

    /// <summary>The result of the document in a GetStatusZip answer: by its code (XmlDocumentKey) or by the name of its XML.</summary>
    public static DianResult? ResultOf(IReadOnlyList<DianResult> results, Document document, DocumentBatch batch)
    {
        var file = FileOf(batch, document);
        return results.FirstOrDefault(result => document.Cufe is { } cufe && string.Equals(result.XmlDocumentKey?.Trim(), cufe, StringComparison.OrdinalIgnoreCase))
            ?? results.FirstOrDefault(result => file is not null && SameFile(result.XmlFileName, file));
    }

    /// <summary>The initial-validation messages SendBillAsync gave about the XML of the document.</summary>
    public static IReadOnlyList<string> MessagesOf(DianResult result, Document document, DocumentBatch batch)
    {
        var file = FileOf(batch, document);
        return file is null ? [] : [.. (result.FileMessages ?? []).Where(message => SameFile(message.FileName, file)).Select(message => message.Message)];
    }

    private static string? FileOf(DocumentBatch batch, Document document) =>
        batch.Entries?.FirstOrDefault(entry => entry.DocumentId == document.Id)?.FileName;

    // The DIAN names the file without its extension (XmlFileName «fv09018462800002500000166»).
    private static bool SameFile(string? named, string file) =>
        named is not null && (string.Equals(named.Trim(), file, StringComparison.OrdinalIgnoreCase)
            || string.Equals(named.Trim(), Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase));

    internal static string? Truncate(string? text, int maxLength) =>
        string.IsNullOrEmpty(text) ? null : text.Length <= maxLength ? text : text[..maxLength];
}

/// <summary>
/// Handler of <c>SendDocumentBatchToDian</c> in the Worker (phase 7a, T8; php-rules 15.2): builds one ZIP with the signed XML of
/// every document of the batch, each named with its own file consecutive and the ZIP with the package consecutive (annex 1.9,
/// sections 6.7 and 6.8), and sends it once with SendBillAsync (fileName = the ZIP name, at most 50 documents and 50 MB,
/// section 7.8.2). The ZipKey is stored in the batch and GetStatusZip is scheduled; the PHP lost it (php-rules 7, row 21).
/// </summary>
/// <remarks>
/// <para>
/// Answers: the ZipKey leaves the batch <c>Sent</c> and its documents <c>Sent</c> (a document whose XML failed the initial
/// validations of SendBillAsync is rejected at once with its message); an answer without ZipKey rejects every document with
/// the messages of the DIAN; a SOAP Fault of the sender fails the batch and its documents with one notice; a DIAN that was not
/// reached leaves the batch <c>Queued</c> and the outbox retries; a call that may have reached the DIAN without an answer is
/// never repeated: each document is followed on its own with GetStatus and its code.
/// </para>
/// <para>
/// Only a batch in <c>Queued</c> is sent, so a repeated message does nothing. The send of the API leaves exactly one message
/// per batch: two sends at the same time cannot both be saved (row version of the batch).
/// </para>
/// </remarks>
internal sealed class SendDocumentBatchToDianHandler(
    BatchDelivery delivery,
    IDocumentBatches batches,
    IDocumentDelivery documents,
    IDocumentStore store,
    INumberingSequences numbering,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    ImpiFactMetrics metrics,
    ILogger<SendDocumentBatchToDianHandler> logger) : IOutboxMessageHandler
{
    public string MessageType => OutboxMessageTypes.SendDocumentBatchToDian;

    public async Task HandleAsync(OutboxMessageContext message, CancellationToken ct)
    {
        var payload = message.GetPayload<DocumentBatchPayload>();
        var companyId = message.CompanyId ?? throw new PermanentOutboxFailureException($"Outbox message {message.MessageId} has no company.");
        var batch = await batches.FindAsync(companyId, payload.BatchId, ct)
            ?? throw new PermanentOutboxFailureException($"Batch {payload.BatchId} does not exist.");
        if (batch.Status != DocumentBatchStatus.Queued)
        {
            logger.LogInformation("Batch {BatchId} is already {Status}; it is not sent again.", batch.Id, batch.Status);
            return;
        }

        var pending = (await batches.DocumentsAsync(companyId, batch.Id, ct)).Where(document => document.Status == DocumentStatus.Queued).ToList();
        if (pending.Count == 0)
        {
            batch.Complete(delivery.Clock.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            return;
        }

        var issuer = await delivery.IssuerAsync(companyId, ct) ?? throw new PermanentOutboxFailureException($"Company {companyId} does not exist.");
        if (!issuer.Company.IsActive)
        {
            // The batch stays Queued with its documents and is looked at again later (see InactiveCompanyHold).
            await InactiveCompanyHold.HoldAsync(message, payload, companyId, outbox, unitOfWork, delivery.Clock, logger, ct);
            return;
        }

        var secretRef = issuer.Certificate?.SecretRef ?? throw new PermanentOutboxFailureException($"Company {companyId} has no active certificate.");
        var endpoint = await delivery.EndpointAsync(companyId, secretRef, batch.TypeEnvironmentId ?? pending[0].TypeEnvironmentId, ct);
        using var certificate = endpoint.Certificate;
        var startedAt = delivery.Clock.UtcNow;

        // Short transaction: the file consecutives, the ZIP and one started attempt per document, saved before the call so a
        // crash in the middle leaves a trace of a call the DIAN may have received.
        byte[] zip;
        var submissions = new Dictionary<Guid, DianSubmission>();
        await using (var transaction = await unitOfWork.BeginTransactionAsync(ct))
        {
            var year = ColombiaTime.Today(startedAt).Year;
            var entries = new List<DianZipEntry>();
            var names = new List<DocumentBatchEntry>();
            foreach (var document in pending)
            {
                var kind = DianFileCounters.KindOf(document);
                var counter = await DianFileCounters.TakeAsync(numbering, unitOfWork, companyId, DianFileNames.Prefix(kind), year, ct);
                var name = DianFileNames.XmlEntry(kind, issuer.Company.IdentificationNumber, year, counter);
                var signed = await store.ReadFileAsync(companyId, document.Id, DocumentFileKind.SignedXml, ct)
                    ?? throw new PermanentOutboxFailureException($"Document {document.Id} has no signed XML.");
                entries.Add(new DianZipEntry(name, signed));
                names.Add(new DocumentBatchEntry { DocumentId = document.Id, FileName = name });
            }

            var zipCounter = await DianFileCounters.TakeAsync(numbering, unitOfWork, companyId, DianFileCounters.ZipPrefix, year, ct);
            zip = DianZip.Create(entries, ColombiaTime.Local(startedAt));
            batch.ZipFileName = DianFileNames.Zip(issuer.Company.IdentificationNumber, year, zipCounter);
            batch.Entries = names;
            if (zip.LongLength > DocumentBatch.MaxZipBytes)
            {
                // Annex 1.9, 7.8.2: contentFile is at most 50 MB. Never reached with 50 documents of a normal size.
                delivery.Fail(batch, pending, $"El ZIP del lote pesa {zip.LongLength} bytes, más que los 50 MB que recibe SendBillAsync.");
                await unitOfWork.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return;
            }

            batch.ZipFileKey = await batches.PutFileAsync(companyId, batch.Id, zip, "application/zip", ".zip", ct);
            foreach (var document in pending)
            {
                submissions[document.Id] = await delivery.BeginAsync(document, DianOperation.SendBillAsync, zipKey: null, ct);
            }

            await unitOfWork.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        var call = await delivery.CallAsync(new SendBillAsyncRequest(batch.ZipFileName!, Convert.ToBase64String(zip)), endpoint, ct);
        var result = call.Response is DianResultResponse { Results: [var first, ..] } ? first : null;
        foreach (var submission in submissions.Values)
        {
            delivery.Finish(submission, call, result);
        }

        batch.ResponseFileKey = await delivery.KeepBodyAsync(batch, call, ct) ?? batch.ResponseFileKey;
        var answer = DianAnswer.From(call);
        if (answer is DianAnswer.Refused refused && await PossiblyArrivedAsync(companyId, pending, submissions, ct))
        {
            // An earlier attempt may have reached the DIAN: the fault may be the DIAN refusing a repetition, not the ZIP.
            answer = new DianAnswer.NoAnswer($"An earlier attempt may have reached the DIAN; this one was refused: {refused.Reason}");
        }

        logger.LogInformation(
            "DIAN SendBillAsync for batch {BatchId} with {Count} documents ended as {Outcome} with HTTP {HttpStatus}: {Answer}.",
            batch.Id, pending.Count, call.Outcome, call.HttpStatus, answer.GetType().Name);
        switch (answer)
        {
            case DianAnswer.NotDelivered notDelivered:
                // The attempts are saved with their result; the batch stays Queued and the Worker retries the message.
                await unitOfWork.SaveChangesAsync(ct);
                throw new DianNotDeliveredException($"The DIAN did not receive batch {batch.Id}: {notDelivered.Reason}");
            case DianAnswer.Refused refusal:
                delivery.Fail(batch, pending, refusal.Reason);
                break;
            case DianAnswer.ZipReceived received:
                await ReceivedAsync(batch, pending, received.ZipKey, result!, startedAt, endpoint, ct);
                break;
            case DianAnswer.Decided rejected:
                await RejectAllAsync(batch, pending, rejected.Result, startedAt, endpoint, ct);
                break;
            default:
                await delivery.FollowOneByOneAsync(batch, pending, startedAt, ct);
                break;
        }

        await unitOfWork.SaveChangesAsync(ct);
        foreach (var document in pending.Where(document => document.Status != DocumentStatus.Failed))
        {
            metrics.DocumentIssued(document);
            if (document.Status is DocumentStatus.Accepted or DocumentStatus.AcceptedWithWarnings or DocumentStatus.Rejected)
            {
                metrics.DocumentDecided(document);
            }
        }
    }

    /// <summary>
    /// The Worker gave up with the message and the batch is still <c>Queued</c>: if an attempt may have reached the DIAN, each
    /// document is followed on its own; if none could, the batch and its documents fail with a notice.
    /// </summary>
    public async Task OnFailedAsync(OutboxMessageContext message, CancellationToken ct)
    {
        var payload = message.GetPayload<DocumentBatchPayload>();
        if (message.CompanyId is not { } companyId
            || await batches.FindAsync(companyId, payload.BatchId, ct) is not { Status: DocumentBatchStatus.Queued } batch)
        {
            return;
        }

        var pending = (await batches.DocumentsAsync(companyId, batch.Id, ct)).Where(document => document.Status == DocumentStatus.Queued).ToList();
        DateTimeOffset? sentAt = null;
        foreach (var document in pending)
        {
            if (await documents.FirstPossibleArrivalAsync(companyId, document.Id, exceptSubmissionId: null, ct) is { } arrival)
            {
                sentAt = sentAt is null || arrival < sentAt ? arrival : sentAt;
            }
        }

        if (sentAt is { } possibly)
        {
            await delivery.FollowOneByOneAsync(batch, pending, possibly, ct);
        }
        else
        {
            delivery.Fail(batch, pending, batch.DianStatusDescription ?? "La DIAN no estuvo disponible en ninguno de los intentos de envío del lote.");
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// SendBillAsync took the ZIP. The documents whose XML the DIAN named in the initial validations are rejected now with
    /// those messages (they never enter the validation queue); the others wait for GetStatusZip.
    /// </summary>
    private async Task ReceivedAsync(
        DocumentBatch batch, IReadOnlyList<Document> pending, string zipKey, DianResult result, DateTimeOffset startedAt, DianEndpoint endpoint,
        CancellationToken ct)
    {
        batch.MarkSent(zipKey, startedAt);
        batch.ErrorMessages = result.ErrorMessages.Count > 0 ? [.. result.ErrorMessages] : null;
        foreach (var document in pending)
        {
            document.MoveTo(DocumentStatus.Sent);
            document.SentAt = startedAt;
            if (BatchDelivery.MessagesOf(result, document, batch) is { Count: > 0 } messages)
            {
                await delivery.DecideAsync(document, InitialRejection(result, messages), endpoint, ct);
            }
        }

        if (pending.All(document => document.Status != DocumentStatus.Sent))
        {
            batch.Complete(delivery.Clock.UtcNow);
            return;
        }

        await delivery.SchedulePollAsync(batch, delivery.Clock.UtcNow + DianPollSchedule.FirstZipPoll, ct);
    }

    /// <summary>SendBillAsync answered without ZipKey: the ZIP was refused whole, so every document is rejected with its messages.</summary>
    private async Task RejectAllAsync(
        DocumentBatch batch, IReadOnlyList<Document> pending, DianResult result, DateTimeOffset startedAt, DianEndpoint endpoint, CancellationToken ct)
    {
        batch.ErrorMessages = result.ErrorMessages.Count > 0 ? [.. result.ErrorMessages] : null;
        batch.DianStatusDescription = "La DIAN rechazó el ZIP del lote en las validaciones iniciales.";
        foreach (var document in pending)
        {
            document.MoveTo(DocumentStatus.Sent);
            document.SentAt = startedAt;
            var messages = BatchDelivery.MessagesOf(result, document, batch);
            await delivery.DecideAsync(document, InitialRejection(result, messages.Count > 0 ? messages : result.ErrorMessages), endpoint, ct);
        }

        batch.Complete(delivery.Clock.UtcNow, startedAt);
    }

    private static DianAnswer.Decided InitialRejection(DianResult result, IReadOnlyList<string> messages) => new(
        DocumentStatus.Rejected,
        result with
        {
            IsValid = false,
            StatusDescription = "La DIAN rechazó el documento en las validaciones iniciales del envío por lotes.",
            ErrorMessages = messages,
            XmlBase64Bytes = null,
        });

    private async Task<bool> PossiblyArrivedAsync(
        Guid companyId, IReadOnlyList<Document> pending, IReadOnlyDictionary<Guid, DianSubmission> current, CancellationToken ct)
    {
        foreach (var document in pending)
        {
            if (await documents.FirstPossibleArrivalAsync(companyId, document.Id, current[document.Id].Id, ct) is not null)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Handler of <c>PollDocumentBatchStatus</c> in the Worker (phase 7a, T8): asks GetStatusZip with the ZipKey of a sent batch
/// and gives each document its own verdict, found by its code (XmlDocumentKey) or by the name of its XML: accepted, with the
/// plan quota, the AttachedDocument, the PDF and the email exactly as a document sent alone; rejected, with its rules and a
/// notice. Rule 90 asks GetStatus with the code of that document. A document is never sent again. Until every document has
/// its verdict the next query follows <see cref="DianPollSchedule"/> (every minute for 10 minutes, then every 15, up to 24
/// hours after the send); after that the documents without verdict fail with the notice of an unconfirmed document.
/// </summary>
internal sealed class PollDocumentBatchStatusHandler(
    BatchDelivery delivery,
    IDocumentBatches batches,
    IUnitOfWork unitOfWork,
    ImpiFactMetrics metrics,
    ILogger<PollDocumentBatchStatusHandler> logger) : IOutboxMessageHandler
{
    public string MessageType => OutboxMessageTypes.PollDocumentBatchStatus;

    public async Task HandleAsync(OutboxMessageContext message, CancellationToken ct)
    {
        var payload = message.GetPayload<DocumentBatchPayload>();
        var companyId = message.CompanyId ?? throw new PermanentOutboxFailureException($"Outbox message {message.MessageId} has no company.");
        var batch = await batches.FindAsync(companyId, payload.BatchId, ct)
            ?? throw new PermanentOutboxFailureException($"Batch {payload.BatchId} does not exist.");
        if (batch.Status != DocumentBatchStatus.Sent)
        {
            logger.LogInformation("Batch {BatchId} is already {Status}; GetStatusZip is not asked again.", batch.Id, batch.Status);
            return;
        }

        var zipKey = batch.ZipKey ?? throw new PermanentOutboxFailureException($"Batch {batch.Id} has no ZipKey.");
        var pending = (await batches.DocumentsAsync(companyId, batch.Id, ct)).Where(document => document.Status == DocumentStatus.Sent).ToList();
        if (pending.Count == 0)
        {
            batch.Complete(delivery.Clock.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            return;
        }

        if ((await delivery.IssuerAsync(companyId, ct))?.Certificate?.SecretRef is not { } secretRef)
        {
            logger.LogWarning("Company {CompanyId} has no active certificate; GetStatusZip of batch {BatchId} is not asked this time.", companyId, batch.Id);
            await delivery.ContinueAsync(batch, pending, ct);
            await unitOfWork.SaveChangesAsync(ct);
            return;
        }

        var endpoint = await delivery.EndpointAsync(companyId, secretRef, batch.TypeEnvironmentId ?? pending[0].TypeEnvironmentId, ct);
        using var certificate = endpoint.Certificate;
        var call = await delivery.CallAsync(new GetStatusZipRequest(zipKey), endpoint, ct);
        var results = call.Response is DianResultResponse { OperationName: "GetStatusZip" } response ? response.Results : [];
        logger.LogInformation(
            "DIAN GetStatusZip for batch {BatchId} ended as {Outcome} with HTTP {HttpStatus} and {Count} results.",
            batch.Id, call.Outcome, call.HttpStatus, results.Count);

        var decided = new List<Document>();
        foreach (var document in pending)
        {
            if (BatchDelivery.ResultOf(results, document, batch) is not { } result)
            {
                continue;
            }

            var submission = await delivery.BeginAsync(document, DianOperation.GetStatusZip, zipKey, ct);
            delivery.Finish(submission, call, result);
            switch (DianAnswer.Classify(result))
            {
                case DianAnswer.Decided verdict:
                    await delivery.KeepApplicationResponseAsync(submission, result, ct);
                    await delivery.DecideAsync(document, verdict, endpoint, ct);
                    decided.Add(document);
                    break;
                case DianAnswer.AlreadyProcessed:
                    // Rule 90: the DIAN already processed a document with that number; its own status settles it.
                    if (await delivery.ReconcileAsync(document, endpoint, ct) is DianAnswer.Decided reconciled)
                    {
                        await delivery.DecideAsync(document, reconciled, endpoint, ct);
                        decided.Add(document);
                    }
                    else
                    {
                        await delivery.FollowOneByOneAsync(batch, [document], document.SentAt ?? delivery.Clock.UtcNow, ct, completeBatch: false);
                    }

                    break;
                default:
                    DianDelivery.Stamp(document, result);
                    break;
            }
        }

        var remaining = pending.Where(document => document.Status == DocumentStatus.Sent).ToList();
        if (remaining.Count == 0)
        {
            batch.Complete(delivery.Clock.UtcNow);
        }
        else
        {
            await delivery.ContinueAsync(batch, remaining, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        foreach (var document in decided)
        {
            metrics.DocumentDecided(document);
        }
    }

    /// <summary>The Worker gave up with the query: another one is scheduled or, past the 24 hours, the documents fail.</summary>
    public async Task OnFailedAsync(OutboxMessageContext message, CancellationToken ct)
    {
        var payload = message.GetPayload<DocumentBatchPayload>();
        if (message.CompanyId is not { } companyId
            || await batches.FindAsync(companyId, payload.BatchId, ct) is not { Status: DocumentBatchStatus.Sent } batch)
        {
            return;
        }

        var pending = (await batches.DocumentsAsync(companyId, batch.Id, ct)).Where(document => document.Status == DocumentStatus.Sent).ToList();
        await delivery.ContinueAsync(batch, pending, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
