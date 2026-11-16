using System.Text.Json.Nodes;
using ImpiFact.Domain.Common;

namespace ImpiFact.Domain.Platform;

// Operación y plataforma (Modelo de datos detallado, sección 3.7). La bandeja de salida y la bitácora de
// auditoría son de la plataforma: su company_id es opcional y no llevan el filtro de empresa.

/// <summary>
/// Mensaje de la bandeja de salida (<c>outbox_messages</c>): trabajo pendiente que el Worker toma en orden, con
/// reintentos y bloqueo por tiempo.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }

    public Guid? CompanyId { get; init; }

    /// <summary>Nombre del tipo de mensaje.</summary>
    public required string Type { get; init; }

    public required JsonNode Payload { get; init; }

    public OutboxStatus Status { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public string? LockedBy { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ProcessedAt { get; set; }
}

/// <summary>Estado de un mensaje de la bandeja de salida.</summary>
public enum OutboxStatus : short
{
    Pending = 0,
    Processing = 1,
    Done = 2,
    Failed = 3,
}

/// <summary>
/// Registro de auditoría (<c>audit_logs</c>) de cambios sensibles; llave <c>bigint</c> que genera la base.
/// <see cref="Changes"/> nunca incluye secretos.
/// </summary>
public sealed class AuditLog
{
    public long Id { get; init; }

    public Guid? CompanyId { get; init; }

    public ActorKind ActorType { get; init; }

    public Guid? ActorId { get; init; }

    public required string Action { get; init; }

    public required string EntityType { get; init; }

    public string? EntityId { get; init; }

    public string? CorrelationId { get; init; }

    public string? Ip { get; init; }

    public string? UserAgent { get; init; }

    public JsonObject? Changes { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Aviso del panel para una empresa o uno de sus usuarios (<c>notifications</c>).</summary>
public sealed class Notification : TenantEntity
{
    /// <summary>Usuario destinatario; nulo para todos los usuarios de la empresa.</summary>
    public Guid? UserId { get; init; }

    public required string Type { get; init; }

    public required string Title { get; init; }

    public required string Body { get; init; }

    public bool IsRead { get; set; }
}

/// <summary>
/// Llave de idempotencia (<c>idempotency_keys</c>): permite reintentar un POST de documento sin emitirlo dos
/// veces; guarda la respuesta original hasta que vence.
/// </summary>
public sealed class IdempotencyKey : TenantEntity
{
    public required string Key { get; init; }

    /// <summary>SHA-256 del cuerpo de la solicitud en hexadecimal (64 caracteres).</summary>
    public required string RequestHash { get; init; }

    public int ResponseStatus { get; set; }

    public JsonNode? ResponseBody { get; set; }

    public DateTimeOffset ExpiresAt { get; init; }
}
