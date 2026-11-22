using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Domain.Documents;
using ImpiFact.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Infrastructure.Documents;

/// <summary>
/// Búsquedas de las rutas utilitarias con EF Core (Entity Framework, el mapeador de objetos a tablas), sin rastreo. Quita
/// solo el filtro de tenant (tenant: cada empresa cliente con sus datos aislados) y pone la empresa en el WHERE, como
/// las demás lecturas de Documents. El contenido de las respuestas guardadas sale del almacenamiento de archivos.
/// </summary>
internal sealed class EfDocumentLookups(ImpiFactDbContext db, IBlobStorage storage) : IDocumentLookups
{
    /// <summary>Operaciones cuya respuesta dice el estado del documento (SendTestSetAsync solo trae la ZipKey).</summary>
    private static readonly DianOperation[] StatusOperations = [DianOperation.SendBillSync, DianOperation.GetStatus, DianOperation.GetStatusZip];

    public Task<DocumentRef?> FindByIdAsync(Guid companyId, Guid documentId, CancellationToken ct) =>
        FirstAsync(Documents(companyId).Where(document => document.Id == documentId), ct);

    public Task<DocumentRef?> FindByCufeAsync(Guid companyId, string cufe, CancellationToken ct) =>
        FirstAsync(Documents(companyId).Where(document => document.Cufe == cufe), ct);

    public async Task<byte[]?> LatestDianAnswerAsync(Guid companyId, Guid documentId, CancellationToken ct)
    {
        var fileId = await Submissions(companyId)
            .Where(submission => submission.DocumentId == documentId && StatusOperations.Contains(submission.Operation))
            .OrderByDescending(submission => submission.Attempt)
            .Select(submission => submission.ResponseFileId)
            .FirstOrDefaultAsync(ct);
        return fileId is { } id ? await ReadFileAsync(companyId, id, ct) : null;
    }

    public async Task<ZipAnswer?> FindZipAnswerAsync(Guid companyId, string zipKey, CancellationToken ct)
    {
        var documentId = await Submissions(companyId, withResponseOnly: false)
            .Where(submission => submission.ZipKey == zipKey)
            .OrderByDescending(submission => submission.StartedAt)
            .Select(submission => (Guid?)submission.DocumentId)
            .FirstOrDefaultAsync(ct);
        if (documentId is null || await FindByIdAsync(companyId, documentId.Value, ct) is not { } document)
        {
            return null;
        }

        var fileId = await Submissions(companyId)
            .Where(submission => submission.ZipKey == zipKey && submission.Operation == DianOperation.GetStatusZip)
            .OrderByDescending(submission => submission.StartedAt)
            .ThenByDescending(submission => submission.Attempt)
            .Select(submission => submission.ResponseFileId)
            .FirstOrDefaultAsync(ct);
        return new ZipAnswer(document, fileId is { } id ? await ReadFileAsync(companyId, id, ct) : null);
    }

    public async Task<int> LatestFileRevisionAsync(Guid companyId, Guid documentId, DocumentFileKind kind, CancellationToken ct) =>
        await Files(companyId)
            .Where(file => file.DocumentId == documentId && file.Kind == kind)
            .MaxAsync(file => (int?)file.Revision, ct) ?? 0;

    private IQueryable<Document> Documents(Guid companyId) =>
        db.Set<Document>().AsNoTracking().IgnoreQueryFilters([QueryFilters.Tenant]).Where(document => document.CompanyId == companyId);

    private IQueryable<DianSubmission> Submissions(Guid companyId, bool withResponseOnly = true) =>
        db.Set<DianSubmission>().AsNoTracking().IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(submission => submission.CompanyId == companyId && (!withResponseOnly || submission.ResponseFileId != null));

    private IQueryable<DocumentFile> Files(Guid companyId) =>
        db.Set<DocumentFile>().AsNoTracking().IgnoreQueryFilters([QueryFilters.Tenant]).Where(file => file.CompanyId == companyId);

    private static async Task<DocumentRef?> FirstAsync(IQueryable<Document> documents, CancellationToken ct) =>
        await documents
            .OrderByDescending(document => document.IssuedAt)
            .ThenByDescending(document => document.Id)
            .Select(document => new DocumentRef(
                document.Id, document.DocumentTypeId, document.TypeEnvironmentId, document.Prefix, document.Number, document.FullNumber,
                document.Status, document.Cufe))
            .FirstOrDefaultAsync(ct);

    private async Task<byte[]?> ReadFileAsync(Guid companyId, Guid fileId, CancellationToken ct)
    {
        var key = await Files(companyId).Where(file => file.Id == fileId).Select(file => file.StorageKey).FirstOrDefaultAsync(ct);
        if (key is null)
        {
            return null;
        }

        await using var stream = await storage.OpenReadAsync(key, ct);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }
}
