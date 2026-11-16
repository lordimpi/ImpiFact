using System.Data.Common;
using System.Text.Json.Nodes;

namespace ImpiFact.Infrastructure.Platform.Outbox;

/// <summary>
/// SQL propio de cada motor para tomar mensajes de la bandeja de salida (outbox: la tabla <c>outbox_messages</c> con
/// el trabajo pendiente del Worker; Arquitectura detallada, sección 8). Cada proyecto
/// <c>ImpiFact.Infrastructure.&lt;Motor&gt;</c> trae su implementación y la registra con su <c>IDatabaseProvider</c>,
/// como el SQL del consecutivo.
/// </summary>
/// <remarks>
/// La toma es una sola sentencia atómica que bloquea las filas elegidas y salta las que otra instancia ya tiene
/// bloqueadas (<c>FOR UPDATE SKIP LOCKED</c> en PostgreSQL, <c>READPAST</c> en SQL Server). Así varias instancias del
/// Worker reparten los mensajes sin esperarse y sin tomar dos veces el mismo.
/// </remarks>
public interface IOutboxQueueSql
{
    /// <summary>
    /// Toma hasta <see cref="OutboxClaim.BatchSize"/> mensajes de <see cref="OutboxClaim.Types"/> que estén pendientes
    /// y vencidos (<c>next_attempt_at &lt;= now</c>) o en proceso con el bloqueo vencido (<c>locked_until &lt; now</c>:
    /// la instancia que los tenía se cayó), los más antiguos primero. Los deja en proceso, a nombre de
    /// <see cref="OutboxClaim.WorkerId"/> hasta <see cref="OutboxClaim.LockedUntil"/>, y suma un intento.
    /// </summary>
    /// <param name="connection">Conexión abierta; la sentencia corre sola, con su propia confirmación.</param>
    /// <returns>Los mensajes tomados, con el número de intentos ya sumado, ordenados por <c>next_attempt_at</c>.</returns>
    Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimAsync(DbConnection connection, OutboxClaim claim, CancellationToken ct);
}

/// <summary>Qué mensajes tomar y a nombre de quién.</summary>
/// <param name="WorkerId">Instancia del Worker que toma los mensajes (columna <c>locked_by</c>).</param>
/// <param name="Types">Tipos de mensaje que esa instancia sabe procesar; los demás nunca se toman.</param>
/// <param name="BatchSize">Máximo de mensajes a tomar.</param>
/// <param name="Now">Instante actual, en UTC.</param>
/// <param name="LockedUntil">Hasta cuándo quedan reservados los mensajes tomados.</param>
public sealed record OutboxClaim(string WorkerId, IReadOnlyCollection<string> Types, int BatchSize, DateTimeOffset Now, DateTimeOffset LockedUntil);

/// <summary>Un mensaje tomado por una instancia del Worker.</summary>
/// <param name="Attempts">Intentos contando el que empieza ahora.</param>
public sealed record ClaimedOutboxMessage(
    Guid Id, Guid? CompanyId, string Type, JsonNode Payload, int Attempts, DateTimeOffset NextAttemptAt);
