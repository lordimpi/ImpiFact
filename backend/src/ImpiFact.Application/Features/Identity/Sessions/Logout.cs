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
    ILogger<LogoutHandler> logger) : IHandler<LogoutCommand, LoggedOut>
{
    public async Task<Result<LoggedOut>> HandleAsync(LogoutCommand request, CancellationToken ct)
    {
        var presented = await refreshStore.FindByHashAsync(refreshTokens.Hash(request.RefreshToken), ct);
        if (presented is null || presented.RevokedAt is not null)
        {
            return new LoggedOut(Revoked: false);
        }

        var revoked = await refreshStore.RevokeAsync(presented.Id, clock.UtcNow, replacedBy: null, ct);
        if (revoked)
        {
            logger.LogInformation("Refresh token {RefreshTokenId} of user {UserId} was revoked by a logout.", presented.Id, presented.UserId);
        }

        return new LoggedOut(revoked);
    }
}
