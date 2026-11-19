namespace ImpiFact.Application.Abstractions.Identity;

/// <summary>
/// Puerto de persistencia de los tokens para restablecer la contraseña (tabla <c>password_reset_tokens</c>). Como con
/// los tokens de renovación, solo se guarda el resumen SHA-256 de cada token, nunca el valor.
/// </summary>
public interface IPasswordResetTokenStore
{
    /// <summary>Agrega un token. Se guarda al confirmar la transacción del caso de uso.</summary>
    Task AddAsync(PasswordResetTokenRecord token, CancellationToken ct);

    /// <summary>
    /// Deja sin efecto (<c>revoked_at</c>) los tokens del usuario que no se usaron ni se revocaron: cuando se pide uno
    /// nuevo, solo el último enlace sirve. Se guarda al confirmar la transacción del caso de uso.
    /// </summary>
    Task RevokeUnusedForUserAsync(Guid userId, DateTimeOffset revokedAt, CancellationToken ct);

    /// <returns>El token con ese resumen, aunque esté usado, revocado o vencido; <see langword="null"/> si no existe.</returns>
    Task<PasswordResetTokenRecord?> FindByHashAsync(string tokenHash, CancellationToken ct);

    /// <summary>
    /// Marca el token como usado (<c>used_at</c>). La fila lleva versión: si dos solicitudes usan el mismo token a la
    /// vez, la segunda en guardar falla por concurrencia. Se guarda al confirmar la transacción del caso de uso.
    /// </summary>
    /// <returns><see langword="false"/> si el token no existe o ya estaba usado o revocado.</returns>
    Task<bool> MarkUsedAsync(Guid tokenId, DateTimeOffset usedAt, CancellationToken ct);

    /// <summary>
    /// El token con el correo y el nombre de su usuario, para que el Worker arme el correo. Lee los datos al momento de
    /// enviar, así no viajan en la bandeja de salida.
    /// </summary>
    /// <returns><see langword="null"/> si el token ya no existe.</returns>
    Task<PasswordResetRecipient?> FindRecipientAsync(Guid tokenId, CancellationToken ct);
}

/// <summary>Un token para restablecer la contraseña tal como está guardado: nunca el valor, solo su resumen.</summary>
/// <param name="RequestedIp">IP (Internet Protocol) desde la que se pidió, hasta 45 caracteres.</param>
public sealed record PasswordResetTokenRecord(
    Guid Id,
    Guid UserId,
    string TokenHash,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? UsedAt,
    DateTimeOffset? RevokedAt,
    string? RequestedIp)
{
    /// <summary>Si todavía sirve en <paramref name="now"/>: no se usó, no se revocó y no venció.</summary>
    public bool IsUsableAt(DateTimeOffset now) => UsedAt is null && RevokedAt is null && ExpiresAt > now;
}

/// <summary>A quién va el correo de un token para restablecer la contraseña.</summary>
/// <param name="Email">Correo del usuario; dato personal, nunca va en los registros.</param>
/// <param name="UserIsActive">Si el usuario sigue activo; a uno inactivo no se le envía el enlace.</param>
public sealed record PasswordResetRecipient(PasswordResetTokenRecord Token, string Email, string Name, bool UserIsActive)
{
    /// <summary>No incluye el correo ni el nombre, para que un registro accidental del objeto no los filtre.</summary>
    public override string ToString() => $"PasswordResetRecipient {{ TokenId = {Token.Id} }}";
}
