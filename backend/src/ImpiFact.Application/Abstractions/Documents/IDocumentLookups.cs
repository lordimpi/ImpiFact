using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Abstractions.Documents;

/// <summary>
/// Búsquedas de las rutas utilitarias de documentos (consultas de estado, descargas, regeneraciones y reenvío del
/// correo): el documento por su id, su CUFE o su número, la última respuesta de la DIAN (la autoridad tributaria de
/// Colombia) que guardó el Worker y la revisión más alta de un archivo. Son lecturas sin rastreo; como los demás
/// puertos con <c>companyId</c>, buscan solo en esa empresa.
/// </summary>
public interface IDocumentLookups
{
    /// <summary>El documento por su id; nulo si no existe en la empresa.</summary>
    Task<DocumentRef?> FindByIdAsync(Guid companyId, Guid documentId, CancellationToken ct);

    /// <summary>
    /// El documento por su CUFE o CUDE (códigos únicos de factura y de documento electrónico); nulo si no existe en la
    /// empresa.
    /// </summary>
    Task<DocumentRef?> FindByCufeAsync(Guid companyId, string cufe, CancellationToken ct);

    /// <summary>
    /// El documento con ese prefijo y número, de cualquier tipo y ambiente; si hay varios, el emitido más
    /// recientemente. Nulo si no hay ninguno.
    /// </summary>
    Task<DocumentRef?> FindByNumberAsync(Guid companyId, string prefix, long number, CancellationToken ct);

    /// <summary>
    /// El documento con ese número completo (prefijo y número) y uno de los tipos indicados; si hay varios, el emitido
    /// más recientemente. Nulo si no hay ninguno.
    /// </summary>
    Task<DocumentRef?> FindByFullNumberAsync(Guid companyId, string fullNumber, IReadOnlyCollection<int> documentTypeIds, CancellationToken ct);

    /// <summary>
    /// Cuerpo de la última respuesta de la DIAN sobre el documento (<c>document_files</c> de tipo 5 de su intento más
    /// reciente con respuesta) en SendBillSync, GetStatus o GetStatusZip; nulo si todavía no hay ninguna. La respuesta
    /// de SendTestSetAsync no cuenta: solo trae la clave del envío.
    /// </summary>
    Task<byte[]?> LatestDianAnswerAsync(Guid companyId, Guid documentId, CancellationToken ct);

    /// <summary>
    /// El documento enviado con esa ZipKey (la clave que devolvió SendTestSetAsync) y el cuerpo de la última respuesta de
    /// GetStatusZip con esa clave, si la hay; nulo si ningún intento de la empresa tiene esa clave.
    /// </summary>
    Task<ZipAnswer?> FindZipAnswerAsync(Guid companyId, string zipKey, CancellationToken ct);

    /// <summary>Revisión más alta guardada de ese tipo de archivo del documento; 0 si no hay ninguna.</summary>
    Task<int> LatestFileRevisionAsync(Guid companyId, Guid documentId, DocumentFileKind kind, CancellationToken ct);
}

/// <summary>Lo que las rutas utilitarias necesitan de un documento para encontrarlo y decidir qué responder.</summary>
public sealed record DocumentRef(
    Guid Id,
    int DocumentTypeId,
    int TypeEnvironmentId,
    string Prefix,
    long Number,
    string FullNumber,
    DocumentStatus Status,
    string? Cufe);

/// <summary>Documento de un envío por ZipKey y la última respuesta de GetStatusZip (nula si aún no la hay).</summary>
public sealed record ZipAnswer(DocumentRef Document, byte[]? Body);
