using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Queries.ExportDocuments;

namespace ImpiFact.Application.Features.Documents.Queries;

/// <summary>Errores esperados de las consultas de documentos.</summary>
public static class DocumentErrors
{
    /// <summary>
    /// El NIT (Número de Identificación Tributaria) de la ruta no es el de la empresa de la credencial. Es el mismo
    /// error exista o no ese NIT, para no revelar qué empresas hay en la plataforma (Arquitectura detallada, sección
    /// 9.2). Lleva el mismo código y texto que el módulo Company para que el cliente vea una sola respuesta.
    /// </summary>
    public static readonly Error ForeignNit = new(
        "company.forbidden",
        "No tiene permiso sobre la empresa indicada.",
        ErrorType.Forbidden);

    /// <summary>El documento no existe en la empresa de la credencial (también si existe en otra empresa).</summary>
    public static readonly Error NotFound = new(
        "document.not_found",
        "No se encontró el documento.",
        ErrorType.NotFound);

    /// <summary>El documento existe pero no tiene guardado el archivo pedido.</summary>
    public static readonly Error FileNotFound = new(
        "document.file_not_found",
        "No se encontró el archivo del documento.",
        ErrorType.NotFound);

    /// <summary>La exportación CSV pedida tiene más filas que el máximo permitido.</summary>
    public static Error ExportTooLarge(long count, int max) => new(
        "documents.export_too_large",
        string.Create(
            DocumentCsv.Numbers,
            $"La exportación tiene {count:N0} documentos y el máximo es {max:N0}. Acote los filtros, por ejemplo con un rango de fechas más corto."),
        ErrorType.Validation);

    /// <summary>Texto del PHP (<c>download</c>) cuando el archivo pedido por nombre no existe.</summary>
    public static Error FileNotFoundNamed(string file) => new(
        "document.file_not_found",
        $"No se encontro el archivo: {file}",
        ErrorType.NotFound);

    /// <summary>
    /// La operación (descargar el XML validado, regenerar el PDF o el AttachedDocument, reenviar el correo) solo vale
    /// para documentos que la DIAN (la autoridad tributaria de Colombia) aceptó, como el PHP, que solo buscaba
    /// documentos con <c>state_document_id = 1</c>.
    /// </summary>
    public static Error NotAccepted(string fullNumber) => new(
        "document.not_accepted",
        $"El documento {fullNumber} no ha sido aceptado por la DIAN.",
        ErrorType.Conflict);
}
