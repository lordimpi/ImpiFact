namespace ImpiFact.Application.Abstractions.Identity;

/// <summary>
/// Puerto de persistencia de los tokens de renovación (tabla <c>refresh_tokens</c>). Solo se guardan sus resúmenes.
/// </summary>
public interface IRefreshTokenStore
{
    /// <summary>Agrega un token. Se guarda al confirmar la transacción del caso de uso.</summary>
    Task AddAsync(RefreshTokenRecord token, CancellationToken ct);

    /// <returns>El token con ese resumen, aunque esté revocado o vencido; <see langword="null"/> si no existe.</returns>
    Task<RefreshTokenRecord?> FindByHashAsync(string tokenHash, CancellationToken ct);

    /// <summary>
    /// Marca el token como revocado. En una rotación, <paramref name="replacedBy"/> apunta al token nuevo; así,
    /// si alguien presenta otra vez el token viejo, se sabe que fue robado y se puede revocar toda la cadena.
    /// Se guarda al confirmar la transacción del caso de uso.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> si el token ya estaba revocado o no existe: otra solicitud lo usó primero, y quien
    /// llama debe tratarlo como reuso.
    /// </returns>
    Task<bool> RevokeAsync(Guid tokenId, DateTimeOffset revokedAt, Guid? replacedBy, CancellationToken ct);

    /// <summary>
    /// Revoca todos los tokens vigentes del usuario y lo guarda de inmediato, en una unidad de trabajo propia,
    /// fuera de la transacción del caso de uso. Se usa cuando alguien presenta un token ya revocado (posible robo):
    /// el caso de uso responde con un error, su transacción se revierte, y esta revocación debe quedar igual.
    /// </summary>
    Task RevokeAllActiveForUserAsync(Guid userId, DateTimeOffset revokedAt, CancellationToken ct);
}

/// <summary>Un token de renovación tal como está guardado: nunca el valor, solo su resumen.</summary>
public sealed record RefreshTokenRecord(
    Guid Id,
    Guid UserId,
    string TokenHash,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    Guid? ReplacedBy,
    string? CreatedIp,
    string? UserAgent);
