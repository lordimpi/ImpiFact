using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Domain.Documents;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Infrastructure.Radian;

/// <summary>
/// RADIAN rows with EF Core (Entity Framework) and their files in the file storage (phase 7a, T6). Every query keeps the
/// tenant filter of the context and also puts the company in the WHERE; a file is read only through a key that starts with
/// the company's folder, which only this class generates.
/// </summary>
internal sealed class EfRadianStore(ImpiFactDbContext db, IBlobStorage storage, ITenantContext tenant) : IRadianStore
{
    public Task<ReceivedDocument?> FindReceivedAsync(Guid companyId, Guid id, CancellationToken ct) =>
        db.Set<ReceivedDocument>().Where(row => row.CompanyId == companyId && row.Id == id).FirstOrDefaultAsync(ct);

    public Task<ReceivedDocument?> FindReceivedByCufeAsync(Guid companyId, string cufe, CancellationToken ct) =>
        db.Set<ReceivedDocument>().Where(row => row.CompanyId == companyId && row.Cufe == cufe).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ReceivedDocument>> ListReceivedAsync(ReceivedDocumentFilter filter, CancellationToken ct) =>
        await Filtered(filter)
            .OrderByDescending(row => row.IssueDate)
            .ThenByDescending(row => row.Id)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .ToListAsync(ct);

    public async Task<long> CountReceivedAsync(ReceivedDocumentFilter filter, CancellationToken ct) =>
        await Filtered(filter).LongCountAsync(ct);

    public void AddReceived(ReceivedDocument document)
    {
        TenantWriteCheck.EnsureCanWrite(tenant, document.CompanyId, nameof(ReceivedDocument));
        db.Set<ReceivedDocument>().Add(document);
    }

    public async Task<IReadOnlyList<DocumentEvent>> EventsOfReceivedAsync(Guid companyId, Guid receivedDocumentId, CancellationToken ct) =>
        await db.Set<DocumentEvent>()
            .Where(row => row.CompanyId == companyId && row.ReceivedDocumentId == receivedDocumentId)
            .OrderBy(row => row.IssuedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<DocumentEvent>> EventsOfReceivedAsync(
        Guid companyId, IReadOnlyCollection<Guid> receivedDocumentIds, CancellationToken ct)
    {
        if (receivedDocumentIds.Count == 0)
        {
            return [];
        }

        return await db.Set<DocumentEvent>().AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.ReceivedDocumentId != null && receivedDocumentIds.Contains(row.ReceivedDocumentId.Value))
            .OrderBy(row => row.IssuedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DocumentEvent>> EventsOfDocumentAsync(Guid companyId, Guid documentId, CancellationToken ct) =>
        await db.Set<DocumentEvent>()
            .Where(row => row.CompanyId == companyId && row.DocumentId == documentId)
            .OrderBy(row => row.IssuedAt)
            .ToListAsync(ct);

    public void AddEvent(DocumentEvent documentEvent)
    {
        TenantWriteCheck.EnsureCanWrite(tenant, documentEvent.CompanyId, nameof(DocumentEvent));
        db.Set<DocumentEvent>().Add(documentEvent);
    }

    public async Task<string> PutFileAsync(
        Guid companyId, Guid ownerId, byte[] content, string contentType, string extension, CancellationToken ct)
    {
        // Checked before writing, so no file of another company lands in the storage.
        TenantWriteCheck.EnsureCanWrite(tenant, companyId, "RADIAN file");
        var key = $"{Folder(companyId)}{ownerId:N}/{Guid.CreateVersion7():N}{extension}";
        using var stream = new MemoryStream(content, writable: false);
        await storage.PutAsync(key, stream, contentType, ct);
        return key;
    }

    public async Task<byte[]?> ReadFileAsync(Guid companyId, string key, CancellationToken ct)
    {
        if (!key.StartsWith(Folder(companyId), StringComparison.Ordinal) || key.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            await using var stream = await storage.OpenReadAsync(key, ct);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            return buffer.ToArray();
        }
        catch (BlobNotFoundException)
        {
            return null;
        }
    }

    private static string Folder(Guid companyId) => $"{companyId:N}/radian/";

    private IQueryable<ReceivedDocument> Filtered(ReceivedDocumentFilter filter)
    {
        var rows = db.Set<ReceivedDocument>().AsNoTracking().Where(row => row.CompanyId == filter.CompanyId);
        if (filter.SupplierNit is { } nit)
        {
            rows = rows.Where(row => row.SupplierIdentificationNumber == nit);
        }

        if (filter.IssuedFrom is { } from)
        {
            rows = rows.Where(row => row.IssueDate >= from);
        }

        if (filter.IssuedUntil is { } until)
        {
            rows = rows.Where(row => row.IssueDate <= until);
        }

        return rows;
    }
}
