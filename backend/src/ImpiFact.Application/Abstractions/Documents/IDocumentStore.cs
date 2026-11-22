using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Abstractions.Documents;

/// <summary>
/// Escritura de documentos emitidos (<c>documents</c> y sus tablas hijas; Modelo de datos detallado, sección 3.5) y
/// las pocas lecturas que la emisión necesita dentro de su transacción: el documento que ya usa un número y el
/// documento al que se refiere una nota. Las filas agregadas se guardan cuando el decorador de transacción guarda los
/// cambios; el caso de uso nunca guarda por su cuenta.
/// </summary>
/// <remarks>
/// Como los demás puertos con <c>companyId</c>, busca solo en esa empresa; la guardia de escritura impide que una
/// solicitud agregue filas de otra.
/// </remarks>
public interface IDocumentStore
{
    /// <summary>Agrega el documento con sus líneas, impuestos, descuentos, cargos, pagos y referencias.</summary>
    void Add(DocumentGraph graph);

    /// <summary>Documento que ya usa ese tipo, ambiente, prefijo y número; nulo si no hay ninguno.</summary>
    Task<StoredDocument?> FindByNumberAsync(Guid companyId, DocumentNumberKey number, CancellationToken ct);

    /// <summary>Documento por su id; nulo si no existe en la empresa.</summary>
    Task<StoredDocument?> FindByIdAsync(Guid companyId, Guid documentId, CancellationToken ct);

    /// <summary>
    /// Id del documento de la empresa con ese CUFE o CUDE (códigos únicos de factura y de documento electrónico);
    /// nulo si no hay ninguno.
    /// </summary>
    Task<Guid?> FindIdByCufeAsync(Guid companyId, string cufe, CancellationToken ct);

    /// <summary>Contenido de la primera revisión de un archivo del documento; nulo si no existe.</summary>
    Task<byte[]?> ReadFileAsync(Guid companyId, Guid documentId, DocumentFileKind kind, CancellationToken ct);
}

/// <summary>Un documento nuevo con todas sus filas hijas, ya enlazadas por <c>document_id</c> y <c>line_id</c>.</summary>
public sealed record DocumentGraph(
    Document Document,
    IReadOnlyList<DocumentLine> Lines,
    IReadOnlyList<DocumentTax> Taxes,
    IReadOnlyList<DocumentLineTax> LineTaxes,
    IReadOnlyList<DocumentAllowanceCharge> AllowanceCharges,
    IReadOnlyList<DocumentPayment> Payments,
    IReadOnlyList<DocumentReference> References);

/// <summary>Lo que la emisión necesita de un documento ya guardado para responder otra vez por él.</summary>
/// <param name="RequestSha256">
/// SHA-256 (huella del contenido) de la solicitud guardada (<c>document_files</c> de tipo solicitud); nulo si no
/// se guardó. Sirve para saber si una solicitud repetida es la misma.
/// </param>
/// <param name="BatchId">Batch the document was issued into (phase 7a, T8); null for a document sent on its own.</param>
public sealed record StoredDocument(
    Guid Id,
    int DocumentTypeId,
    int TypeEnvironmentId,
    Guid? ResolutionId,
    string Prefix,
    long Number,
    string FullNumber,
    DocumentStatus Status,
    string? Cufe,
    string? UuidAlgorithm,
    string? RequestSha256,
    Guid? BatchId = null);
