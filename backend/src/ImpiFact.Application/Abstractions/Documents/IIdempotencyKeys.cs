namespace ImpiFact.Application.Abstractions.Documents;

/// <summary>
/// Llaves de idempotencia (<c>idempotency_keys</c>): el encabezado <c>Idempotency-Key</c> permite reintentar la
/// emisión de un documento sin emitirlo dos veces (Arquitectura detallada, sección 7, paso 1). Cada llave queda ligada
/// al resumen de la solicitud y al documento que se emitió con ella.
/// </summary>
/// <remarks>
/// La llave es única por empresa. Si dos solicitudes con la misma llave llegan a la vez, la segunda choca con el
/// índice único al guardar y el decorador de transacción responde 409. Las llaves vencidas las purga el proceso de
/// mantenimiento (fase 4); mientras existan, siguen respondiendo por su documento.
/// </remarks>
public interface IIdempotencyKeys
{
    /// <summary>Llave guardada de la empresa; nula si no existe.</summary>
    Task<IdempotencyRecord?> FindAsync(Guid companyId, string key, CancellationToken ct);

    /// <summary>Agrega la llave; se guarda con la transacción de la solicitud.</summary>
    void Add(Guid companyId, string key, string requestSha256, Guid documentId, DateTimeOffset expiresAt);
}

/// <summary>Llave guardada.</summary>
/// <param name="RequestSha256">SHA-256 de la solicitud con que se usó la llave, en hexadecimal minúsculo.</param>
/// <param name="DocumentId">Documento emitido con la llave.</param>
public sealed record IdempotencyRecord(string RequestSha256, Guid? DocumentId);
