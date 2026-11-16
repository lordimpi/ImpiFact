using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Features.Documents.Queries;

/// <summary>
/// Estado heredado <c>state_document_id</c> del PHP, que no tenía catálogo (Modelo de datos detallado, sección 4):
/// 1 = validado por la DIAN (Dirección de Impuestos y Aduanas Nacionales), 2 = contingencia pendiente y 0 todo lo
/// demás, incluidos los rechazados.
/// </summary>
public static class LegacyDocumentState
{
    /// <summary>Estados que el PHP guardaba como <c>state_document_id = 1</c>.</summary>
    public static IReadOnlyCollection<DocumentStatus> Validated { get; } = [DocumentStatus.Accepted, DocumentStatus.AcceptedWithWarnings];

    public static int Of(DocumentStatus status) => status switch
    {
        DocumentStatus.Accepted or DocumentStatus.AcceptedWithWarnings => 1,
        DocumentStatus.ContingencyPending => 2,
        _ => 0,
    };
}
