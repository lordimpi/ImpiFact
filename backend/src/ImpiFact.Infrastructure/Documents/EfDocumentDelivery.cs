using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Domain.Documents;
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
/// guardado falla por concurrencia y el mensaje se reintenta, en lugar de pisar el estado.
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
}
