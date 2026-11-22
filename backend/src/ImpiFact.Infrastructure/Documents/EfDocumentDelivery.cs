using System.Text.Json.Nodes;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Infrastructure.Documents;

/// <summary>
/// Documento con rastreo de cambios e intentos contra la DIAN (<c>dian_submissions</c>) sobre el contexto de EF Core
/// (Entity Framework, el mapeador de objetos a tablas) del ámbito. Quita solo el filtro de tenant (tenant: cada empresa
/// cliente con sus datos aislados) y pone la empresa en el WHERE, como las demás lecturas de Documents.
/// </summary>
/// <remarks>
/// El documento lleva <c>row_version</c>: si otra instancia del Worker lo cambió entre la lectura y el guardado, el
/// guardado falla por concurrencia y el mensaje se reintenta, en lugar de pisar el estado. <see cref="Claim"/> usa esa
/// misma versión para que un solo mensaje de envío del documento llame a la DIAN.
/// </remarks>
internal sealed class EfDocumentDelivery(ImpiFactDbContext db, ITenantContext tenant) : IDocumentDelivery
{
    public Task<Document?> FindForUpdateAsync(Guid companyId, Guid documentId, CancellationToken ct) =>
        db.Set<Document>()
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .FirstOrDefaultAsync(document => document.CompanyId == companyId && document.Id == documentId, ct);

    public async Task<int> NextSubmissionAttemptAsync(Guid companyId, Guid documentId, CancellationToken ct)
    {
        var stored = await db.Set<DianSubmission>()
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(submission => submission.CompanyId == companyId && submission.DocumentId == documentId)
            .MaxAsync(submission => (int?)submission.Attempt, ct) ?? 0;
        var added = db.Set<DianSubmission>().Local
            .Where(submission => submission.CompanyId == companyId && submission.DocumentId == documentId)
            .Select(submission => submission.Attempt)
            .DefaultIfEmpty(0)
            .Max();
        return Math.Max(stored, added) + 1;
    }

    public void AddSubmission(DianSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        TenantWriteCheck.EnsureCanWrite(tenant, submission.CompanyId, nameof(DianSubmission));
        db.Set<DianSubmission>().Add(submission);
    }

    public Task<DateTimeOffset?> FirstPossibleArrivalAsync(Guid companyId, Guid documentId, Guid? exceptSubmissionId, CancellationToken ct) =>
        db.Set<DianSubmission>()
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .AsNoTracking()
            .Where(submission => submission.CompanyId == companyId && submission.DocumentId == documentId
                && submission.Id != exceptSubmissionId
                && (submission.FinishedAt == null
                    || submission.IsValid != null
                    || submission.Operation == DianOperation.GetStatus
                    || submission.Operation == DianOperation.GetStatusZip))
            .MinAsync(submission => (DateTimeOffset?)submission.StartedAt, ct);

    public async Task<IReadOnlyList<string>> InvalidSendMessagesAsync(Guid companyId, Guid documentId, CancellationToken ct)
    {
        // error_messages es JSON convertido por EF: se trae y se aplana en memoria, igual en todos los motores.
        var answers = await db.Set<DianSubmission>()
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .AsNoTracking()
            .Where(submission => submission.CompanyId == companyId && submission.DocumentId == documentId
                && submission.IsValid == false
                && (submission.Operation == DianOperation.SendBillSync || submission.Operation == DianOperation.SendTestSetAsync))
            .OrderBy(submission => submission.Attempt)
            .Select(submission => new { submission.ErrorMessages, submission.DianStatusDescription })
            .ToListAsync(ct);
        return
        [
            .. answers.SelectMany(answer => (answer.ErrorMessages ?? []).Append(answer.DianStatusDescription))
                .OfType<string>(),
        ];
    }

    public async Task<IReadOnlyList<Document>> ListContingencyPendingAsync(Guid companyId, string? prefix, long? number, CancellationToken ct)
    {
        var query = db.Set<Document>()
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .AsNoTracking()
            .Where(document => document.CompanyId == companyId
                && document.DocumentTypeId == DocumentFamilies.DianContingencyInvoiceType
                && document.Status == DocumentStatus.ContingencyPending);
        if (prefix is not null)
        {
            query = query.Where(document => document.Prefix == prefix);
        }

        if (number is not null)
        {
            query = query.Where(document => document.Number == number);
        }

        return await query.OrderBy(document => document.Prefix).ThenBy(document => document.Number).ToListAsync(ct);
    }

    // Marking updated_at as changed puts the row in the UPDATE; the audit interceptor then stamps updated_at and the next
    // row_version, with the version that was read in the WHERE (optimistic concurrency).
    public void Claim(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        db.Entry(document).Property(nameof(Document.UpdatedAt)).IsModified = true;
    }

    public Task<long?> StoredRowVersionAsync(Guid companyId, Guid documentId, CancellationToken ct) =>
        db.Set<Document>()
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .AsNoTracking()
            .Where(document => document.CompanyId == companyId && document.Id == documentId)
            .Select(document => (long?)document.RowVersion)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<LiveDianSend>> LiveSendsAsync(Guid companyId, IReadOnlyCollection<Guid> documentIds, CancellationToken ct)
    {
        if (documentIds.Count == 0)
        {
            return [];
        }

        // The payload is JSON converted by EF: the company's unfinished sends are few, so they are read and matched in
        // memory, the same in every engine.
        var messages = await db.Set<OutboxMessage>()
            .AsNoTracking()
            .Where(message => message.CompanyId == companyId
                && message.Type == OutboxMessageTypes.SendDocumentToDian
                && (message.Status == OutboxStatus.Pending || message.Status == OutboxStatus.Processing))
            .OrderBy(message => message.CreatedAt)
            .Select(message => new { message.Id, message.Payload, message.Status })
            .ToListAsync(ct);
        var wanted = documentIds.ToHashSet();
        return
        [
            .. messages
                .Select(message => (message.Id, DocumentId: DocumentIdOf(message.Payload), message.Status))
                .Where(message => message.DocumentId is { } id && wanted.Contains(id))
                .Select(message => new LiveDianSend(message.Id, message.DocumentId!.Value, message.Status == OutboxStatus.Processing)),
        ];
    }

    public Task ExpediteSendAsync(Guid messageId, DateTimeOffset now, CancellationToken ct) =>
        db.Set<OutboxMessage>()
            .Where(message => message.Id == messageId && message.Status == OutboxStatus.Pending && message.NextAttemptAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(message => message.NextAttemptAt, now), ct);

    private static Guid? DocumentIdOf(JsonNode payload) =>
        payload["document_id"] is JsonValue value && value.TryGetValue<string>(out var text) && Guid.TryParse(text, out var id) ? id : null;
}
