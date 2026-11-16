namespace ImpiFact.Application.Abstractions.Identity;

/// <summary>
/// Puerto de persistencia de los tokens de renovación (tabla <c>refresh_tokens</c>). El adaptador sobre la tabla
/// y los casos de uso de inicio de sesión y renovación llegan con Identity; aquí queda el contrato.
/// </summary>
public interface IRefreshTokenStore
{
    Task AddAsync(RefreshTokenRecord token, CancellationToken ct);

    /// <returns>El token con ese resumen, aunque esté revocado o vencido; <see langword="null"/> si no existe.</returns>
    Task<RefreshTokenRecord?> FindByHashAsync(string tokenHash, CancellationToken ct);

    /// <summary>
    /// Marca el token como revocado. En una rotación, <paramref name="replacedBy"/> apunta al token nuevo; así,
    /// si alguien presenta otra vez el token viejo, se sabe que fue robado y se puede revocar toda la cadena.
    /// </summary>
    Task RevokeAsync(Guid tokenId, DateTimeOffset revokedAt, Guid? replacedBy, CancellationToken ct);
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
