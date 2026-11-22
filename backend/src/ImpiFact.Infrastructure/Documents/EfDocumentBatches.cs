using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Domain.Documents;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Infrastructure.Documents;

/// <summary>
/// Batches with EF Core (Entity Framework) and their files in the file storage (phase 7a, T8). Every query removes only the
/// tenant filter and puts the company in the WHERE, like the delivery of documents, so the Worker (a system process) reads
/// the same rows as the API; a file key always starts with the company's folder.
/// </summary>
internal sealed class EfDocumentBatches(ImpiFactDbContext db, IBlobStorage storage, ITenantContext tenant) : IDocumentBatches
{
    public Task<DocumentBatch?> FindAsync(Guid companyId, Guid id, CancellationToken ct) =>
        Batches(companyId).FirstOrDefaultAsync(batch => batch.Id == id, ct);

    public Task<DocumentBatch?> FindByNameAsync(Guid companyId, string name, CancellationToken ct) =>
        Batches(companyId).FirstOrDefaultAsync(batch => batch.Name == name, ct);

    public void Add(DocumentBatch batch)
    {
        TenantWriteCheck.EnsureCanWrite(tenant, batch.CompanyId, nameof(DocumentBatch));
        db.Set<DocumentBatch>().Add(batch);
    }

    public async Task<IReadOnlyList<DocumentBatch>> ListAsync(Guid companyId, int skip, int take, CancellationToken ct)
    {
        // The creation time orders the batches and the unique name breaks the ties of the same instant, in every engine.
        return await Batches(companyId).AsNoTracking()
            .OrderByDescending(batch => batch.CreatedAt).ThenBy(batch => batch.Name)
            .Skip(skip).Take(take).ToListAsync(ct);
    }

    public async Task<long> CountAsync(Guid companyId, CancellationToken ct) => await Batches(companyId).LongCountAsync(ct);

    public async Task<IReadOnlyList<Document>> DocumentsAsync(Guid companyId, Guid batchId, CancellationToken ct)
    {
        var rows = await db.Set<Document>()
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(document => document.CompanyId == companyId && document.BatchId == batchId)
            .ToListAsync(ct);
        // Added order: the creation time; documents of the same instant (a fixed clock in tests) by type and number, which a
        // batch of one type takes in the order they were added. Never by the id: uuid v7 of the same millisecond are random.
        return [.. rows.OrderBy(document => document.CreatedAt).ThenBy(document => document.DocumentTypeId).ThenBy(document => document.Prefix, StringComparer.Ordinal)
            .ThenBy(document => document.Number)];
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> LastMessagesAsync(
        Guid companyId, IReadOnlyCollection<Guid> documentIds, CancellationToken ct)
    {
        if (documentIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<string>>();
        }

        // error_messages is JSON converted by EF: it is read and filtered in memory, the same in every engine.
        var answers = await db.Set<DianSubmission>()
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .AsNoTracking()
            .Where(submission => submission.CompanyId == companyId && documentIds.Contains(submission.DocumentId) && submission.FinishedAt != null)
            .Select(submission => new { submission.DocumentId, submission.Attempt, submission.ErrorMessages })
            .ToListAsync(ct);
        return answers
            .Where(answer => answer.ErrorMessages is { Count: > 0 })
            .GroupBy(answer => answer.DocumentId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group.OrderByDescending(answer => answer.Attempt).First().ErrorMessages!);
    }

    public async Task<string> PutFileAsync(Guid companyId, Guid batchId, byte[] content, string contentType, string extension, CancellationToken ct)
    {
        // Checked before writing, so no file of another company lands in the storage.
        TenantWriteCheck.EnsureCanWrite(tenant, companyId, "batch file");
        var key = $"{companyId:N}/batches/{batchId:N}/{Guid.CreateVersion7():N}{extension}";
        using var stream = new MemoryStream(content, writable: false);
        await storage.PutAsync(key, stream, contentType, ct);
        return key;
    }

    private IQueryable<DocumentBatch> Batches(Guid companyId) =>
        db.Set<DocumentBatch>().IgnoreQueryFilters([QueryFilters.Tenant]).Where(batch => batch.CompanyId == companyId);
}
