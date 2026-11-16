using ImpiFact.Application.Abstractions.Results;

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
}
