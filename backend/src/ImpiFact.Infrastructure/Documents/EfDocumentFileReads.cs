using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Domain.Documents;
using ImpiFact.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Infrastructure.Documents;

/// <summary>
/// Revisión más reciente de un archivo de documento: la fila de <c>document_files</c> con EF Core (Entity Framework,
/// el mapeador de objetos a tablas), sin rastreo y con la empresa en el WHERE, y el contenido del almacenamiento de
/// archivos.
/// </summary>
internal sealed class EfDocumentFileReads(ImpiFactDbContext db, IBlobStorage storage) : IDocumentFileReads
{
    public async Task<byte[]?> ReadLatestAsync(Guid companyId, Guid documentId, DocumentFileKind kind, CancellationToken ct)
    {
        var key = await db.Set<DocumentFile>()
            .AsNoTracking()
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(file => file.CompanyId == companyId && file.DocumentId == documentId && file.Kind == kind)
            .OrderByDescending(file => file.Revision)
            .Select(file => file.StorageKey)
            .FirstOrDefaultAsync(ct);
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
