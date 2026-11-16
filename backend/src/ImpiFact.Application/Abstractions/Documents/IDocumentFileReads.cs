using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Abstractions.Documents;

/// <summary>
/// Lectura de los archivos de un documento (<c>document_files</c>) en su revisión más reciente, por ejemplo el PDF o
/// el AttachedDocument (contenedor electrónico) después de regenerarlos. Como los demás puertos con
/// <c>companyId</c>, busca solo en esa empresa.
/// </summary>
public interface IDocumentFileReads
{
    /// <summary>Contenido de la revisión más alta de ese tipo de archivo; nulo si el documento no tiene ninguno.</summary>
    Task<byte[]?> ReadLatestAsync(Guid companyId, Guid documentId, DocumentFileKind kind, CancellationToken ct);
}
