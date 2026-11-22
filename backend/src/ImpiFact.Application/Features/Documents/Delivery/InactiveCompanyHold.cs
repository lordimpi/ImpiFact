using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Abstractions.Persistence;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Features.Documents.Delivery;

/// <summary>
/// What the Worker does with a send to the DIAN of a company the platform deactivated (phase 7a, T9b1; decision L17: an
/// inactive company issues nothing). <c>SendDocumentToDian</c> (a <c>Queued</c> document or a pending invoice type 04) and
/// <c>SendDocumentBatchToDian</c> do not call the DIAN, take no file counter and record no attempt: the document or batch
/// keeps its status and the same message is scheduled again after <see cref="RetryInterval"/>, so the work issued before
/// the deactivation is sent by itself once the company is reactivated, and never while it is inactive. Nothing fails, so
/// no notice is sent and the document never ends <c>Failed</c>. The status polls (<c>PollDianStatus</c>,
/// <c>PollDocumentBatchStatus</c>) keep running: those documents already reached the DIAN. A document keeps a single held
/// message: <c>SendDocumentToDianHandler</c> ends a repeated one instead of scheduling it again (end-of-7a verification, M1:
/// the duplicates waited together and both called the DIAN after the reactivation).
/// </summary>
internal static class InactiveCompanyHold
{
    /// <summary>Wait before looking at the company again: the same 30 minutes the contingency monitoring uses.</summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(30);

    public static async Task HoldAsync<TPayload>(
        OutboxMessageContext message,
        TPayload payload,
        Guid companyId,
        IOutbox outbox,
        IUnitOfWork unitOfWork,
        IClock clock,
        ILogger logger,
        CancellationToken ct)
    {
        var next = clock.UtcNow + RetryInterval;
        await outbox.EnqueueAsync(message.Type, payload, companyId, next, ct);
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogWarning(
            "Company {CompanyId} is inactive: outbox message {MessageId} of type {MessageType} sent nothing to the DIAN and is scheduled again at {NextAttemptAt}.",
            companyId, message.MessageId, message.Type, next);
    }
}
