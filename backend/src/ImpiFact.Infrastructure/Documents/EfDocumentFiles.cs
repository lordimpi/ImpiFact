using System.Security.Cryptography;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Domain.Documents;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Platform;

namespace ImpiFact.Infrastructure.Documents;

/// <summary>
/// Guarda los archivos de un documento en el almacenamiento de archivos y agrega su fila de <c>document_files</c>
/// al contexto de EF Core (Entity Framework) de la solicitud, que guarda el decorador de transacción.
/// </summary>
/// <remarks>
/// La clave es <c>{empresa}/documents/{documento}/{id de la fila}.{extensión}</c>: el id de la fila es nuevo en cada
/// llamada, así dos intentos simultáneos de la misma revisión nunca escriben el mismo archivo; el que pierde contra
/// el índice único <c>(document_id, kind, revision)</c> deja solo un archivo huérfano, nunca uno cambiado.
/// </remarks>
internal sealed class EfDocumentFiles(ImpiFactDbContext db, IBlobStorage storage, ITenantContext tenant) : IDocumentFiles
{
    public async Task<DocumentFile> AddAsync(NewDocumentFile file, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);
        // Se revisa antes de escribir el archivo, para no dejar en el almacén archivos de otra empresa.
        TenantWriteCheck.EnsureCanWrite(tenant, file.CompanyId, nameof(DocumentFile));

        var id = Guid.CreateVersion7();
        var key = $"{file.CompanyId:N}/documents/{file.DocumentId:N}/{id:N}{ExtensionOf(file.Kind)}";
        using (var content = new MemoryStream(file.Content, writable: false))
        {
            await storage.PutAsync(key, content, file.ContentType, ct);
        }

        var row = new DocumentFile
        {
            Id = id,
            CompanyId = file.CompanyId,
            DocumentId = file.DocumentId,
            Kind = file.Kind,
            Revision = file.Revision,
            StorageProvider = storage.Provider,
            StorageKey = key,
            ContentType = file.ContentType,
            SizeBytes = file.Content.LongLength,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(file.Content)),
        };
        db.Set<DocumentFile>().Add(row);
        return row;
    }

    private static string ExtensionOf(DocumentFileKind kind) => kind switch
    {
        DocumentFileKind.Pdf => ".pdf",
        DocumentFileKind.RequestJson => ".json",
        DocumentFileKind.SentZip => ".zip",
        _ => ".xml",
    };
}
