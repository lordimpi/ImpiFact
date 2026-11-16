namespace ImpiFact.Domain.Documents;

/// <summary>
/// Transiciones permitidas entre estados del documento (Modelo de datos detallado, sección 4):
/// <c>Received → {Queued, ContingencyPending, Failed}</c>; <c>Queued → {Sent, Failed}</c>;
/// <c>ContingencyPending → Sent</c>; <c>Sent → {Accepted, AcceptedWithWarnings, Rejected, Unconfirmed}</c>;
/// <c>Unconfirmed → {Accepted, AcceptedWithWarnings, Rejected, Failed}</c>. <c>Draft</c> (vista previa) no tiene
/// transiciones porque nunca se guarda. Cualquier otra transición es un error de dominio.
/// </summary>
public static class DocumentStatusTransitions
{
    private static readonly Dictionary<DocumentStatus, DocumentStatus[]> Allowed = new()
    {
        [DocumentStatus.Received] = [DocumentStatus.Queued, DocumentStatus.ContingencyPending, DocumentStatus.Failed],
        [DocumentStatus.Queued] = [DocumentStatus.Sent, DocumentStatus.Failed],
        [DocumentStatus.ContingencyPending] = [DocumentStatus.Sent],
        [DocumentStatus.Sent] =
        [
            DocumentStatus.Accepted, DocumentStatus.AcceptedWithWarnings, DocumentStatus.Rejected, DocumentStatus.Unconfirmed,
        ],
        [DocumentStatus.Unconfirmed] =
        [
            DocumentStatus.Accepted, DocumentStatus.AcceptedWithWarnings, DocumentStatus.Rejected, DocumentStatus.Failed,
        ],
    };

    /// <summary>Estados a los que se puede pasar desde <paramref name="from"/>; vacío si es un estado final.</summary>
    // Se entrega una vista de solo lectura: el arreglo interno es compartido por todo el proceso.
    public static IReadOnlyList<DocumentStatus> Next(DocumentStatus from) =>
        Allowed.TryGetValue(from, out var next) ? Array.AsReadOnly(next) : Array.AsReadOnly(Array.Empty<DocumentStatus>());

    public static bool IsAllowed(DocumentStatus from, DocumentStatus to) => Next(from).Contains(to);
}

/// <summary>
/// Se intentó una transición de estado que la tabla no permite (por ejemplo, de <c>Accepted</c> a <c>Queued</c>).
/// Es un error de programación del caso de uso, no un flujo esperado.
/// </summary>
public sealed class InvalidDocumentTransitionException(DocumentStatus from, DocumentStatus to)
    : InvalidOperationException($"A document cannot move from {from} to {to}.")
{
    public DocumentStatus From { get; } = from;

    public DocumentStatus To { get; } = to;
}
