using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Abstractions.Documents;

/// <summary>
/// Archivos de un documento (<c>document_files</c>): guarda el contenido en el almacenamiento de archivos
/// (<c>IBlobStorage</c>) y registra la fila con el tipo, el proveedor, la clave, el tipo de contenido, el tamaño y
/// el resumen SHA-256 (huella del contenido que permite comprobar después que no cambió).
/// </summary>
/// <remarks>
/// El archivo se escribe de inmediato y la fila se guarda con la transacción de la solicitud. Si la transacción se
/// revierte, el archivo queda huérfano pero nunca reemplaza a otro: cada archivo lleva una clave nueva, derivada del
/// id de su fila. El índice único <c>(document_id, kind, revision)</c> impide registrar dos veces la misma revisión.
/// </remarks>
public interface IDocumentFiles
{
    /// <summary>Guarda el archivo y agrega su fila; devuelve la fila agregada.</summary>
    /// <exception cref="InvalidOperationException">Si la solicitud es de otra empresa.</exception>
    Task<DocumentFile> AddAsync(NewDocumentFile file, CancellationToken ct);
}

/// <summary>Archivo nuevo de un documento.</summary>
/// <param name="Content">Contenido completo, por ejemplo los bytes del XML firmado.</param>
/// <param name="ContentType">Tipo de contenido, por ejemplo <c>application/xml</c>.</param>
/// <param name="Revision">Revisión del archivo de ese tipo; la primera es 1.</param>
public sealed record NewDocumentFile(
    Guid CompanyId, Guid DocumentId, DocumentFileKind Kind, byte[] Content, string ContentType, int Revision = 1);
