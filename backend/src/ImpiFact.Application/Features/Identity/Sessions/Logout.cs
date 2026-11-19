using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Results;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Features.Identity.Sessions;

/// <summary>
/// Cierre de sesión del panel (Arquitectura frontend, sección 6): revoca el token de renovación (refresh token) que
/// presenta el cliente, así esa sesión ya no puede pedir tokens de acceso nuevos. El token de acceso (JWT) que ya
/// tenga sigue valiendo hasta que venza (15 minutos como máximo); por eso el panel lo borra de la memoria.
/// </summary>
/// <param name="RefreshToken">El token de renovación de la sesión que se cierra.</param>
public sealed record LogoutCommand(string RefreshToken) : ICommand, ICompanyOptional
{
    /// <summary>No incluye el token, para que un registro accidental del objeto no lo filtre.</summary>
    public override string ToString() => "LogoutCommand";
}

/// <param name="Revoked">
/// <see langword="true"/> si esta solicitud revocó el token; <see langword="false"/> si no existía o ya estaba revocado.
/// </param>
public sealed record LoggedOut(bool Revoked);

/// <summary>
/// Cada sesión es una cadena de tokens de renovación: cada renovación revoca el anterior y crea uno nuevo
/// (rotación), así que en una cadena solo el último está vigente. Revocar ese token cierra la cadena completa. Las
/// demás sesiones del usuario (otros navegadores u otros equipos) siguen vivas.
/// </summary>
/// <remarks>
/// Es idempotente: un token desconocido o ya revocado responde bien y no cambia nada. A diferencia de la renovación,
/// presentar aquí un token ya revocado no se trata como robo, porque cerrar la sesión dos veces (dos pestañas, un
/// reintento) es normal y no le da nada a quien lo intente.
/// </remarks>
internal sealed class LogoutHandler(
    IRefreshTokenStore refreshStore,
    IRefreshTokenFactory refreshTokens,
    IClock clock,
    ISessionPolicy policy,
    ILogger<LogoutHandler> logger) : IHandler<LogoutCommand, LoggedOut>
{
    public async Task<Result<LoggedOut>> HandleAsync(LogoutCommand request, CancellationToken ct)
    {
        var presented = await refreshStore.FindByHashAsync(refreshTokens.Hash(request.RefreshToken), ct);
        if (presented is null)
        {
            return new LoggedOut(Revoked: false);
        }

        if (presented.RevokedAt is not null)
        {
            return new LoggedOut(await CloseUnusedReplacementAsync(presented, ct));
        }

        var revoked = await refreshStore.RevokeAsync(presented.Id, clock.UtcNow, replacedBy: null, ct);
        if (revoked)
        {
            logger.LogInformation("Refresh token {RefreshTokenId} of user {UserId} was revoked by a logout.", presented.Id, presented.UserId);
        }

        return new LoggedOut(revoked);
    }

    /// <summary>
    /// Si la respuesta de una renovación se perdió, la cookie trae el token viejo y la sesión sigue en su reemplazo,
    /// que nunca se usó: dentro del período de gracia, cerrar la sesión revoca ese reemplazo. Sin esto, la sesión
    /// quedaba abierta y el token viejo podía recuperarla con la regla de gracia de la renovación.
    /// </summary>
    private async Task<bool> CloseUnusedReplacementAsync(RefreshTokenRecord presented, CancellationToken ct)
    {
        var now = clock.UtcNow;
        if (presented is not { ReplacedBy: { } replacementId, RevokedAt: { } rotatedAt }
            || !SessionRotation.WithinGrace(rotatedAt, now, policy.RotationGracePeriod)
            || await refreshStore.FindByIdAsync(replacementId, ct) is not { RevokedAt: null } replacement
            || replacement.UserId != presented.UserId)
        {
            return false;
        }

        var revoked = await refreshStore.RevokeAsync(replacement.Id, now, replacedBy: null, ct);
        if (revoked)
        {
            logger.LogInformation(
                "Refresh token {RefreshTokenId} of user {UserId}, the unused replacement of the presented one, was revoked by a logout.",
                replacement.Id, replacement.UserId);
        }

        return revoked;
    }
}

/// <summary>Período de gracia de la rotación de tokens de renovación (ver <c>RefreshTokenHandler</c>).</summary>
internal static class SessionRotation
{
    /// <summary>
    /// Si una rotación ocurrida en <paramref name="rotatedAt"/> está dentro del período de gracia. Una rotación con
    /// fecha futura (otra réplica con el reloj adelantado o un ajuste del reloj) no cuenta como reciente: así el
    /// desfase no amplía la ventana ni la abre con una gracia de cero.
    /// </summary>
    public static bool WithinGrace(DateTimeOffset rotatedAt, DateTimeOffset now, TimeSpan grace) =>
        rotatedAt <= now && now - rotatedAt < grace;
}
