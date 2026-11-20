using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Results;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Features.Identity.Sessions;

/// <summary>
/// Renovación de la sesión con un token de renovación (refresh token): entrega un token de acceso nuevo y otro de
/// renovación, y revoca el usado (rotación; Arquitectura detallada, sección 9.2). También sirve para cambiar de
/// empresa activa: se pide con <paramref name="CompanyId"/>.
/// </summary>
/// <param name="RefreshToken">El token de renovación que entregó el inicio de sesión o la renovación anterior.</param>
/// <param name="CompanyId">Empresa activa para el token nuevo; opcional, se valida contra las del usuario.</param>
public sealed record RefreshTokenCommand(string RefreshToken, Guid? CompanyId, string? ClientIp, string? UserAgent)
    : ICommand, ICompanyOptional
{
    /// <summary>No incluye el token, para que un registro accidental del objeto no lo filtre.</summary>
    public override string ToString() => $"RefreshTokenCommand {{ CompanyId = {CompanyId} }}";
}

internal sealed class RefreshTokenValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenValidator() =>
        RuleFor(command => command.RefreshToken)
            .NotEmpty().WithMessage("El token de renovación es obligatorio.")
            .MaximumLength(128).WithMessage("El token de renovación admite hasta 128 caracteres.")
            .OverridePropertyName("refresh_token");
}

/// <summary>
/// Si alguien presenta un token ya revocado, es señal de robo (el dueño legítimo ya lo había cambiado por uno
/// nuevo): se revocan todos los tokens vigentes del usuario, que tendrá que volver a iniciar sesión. Esa
/// revocación se guarda aparte, porque este caso de uso responde con un error y su transacción se revierte.
/// </summary>
/// <remarks>
/// <para>
/// Período de gracia de la rotación (<see cref="ISessionPolicy.RotationGracePeriod"/>, 30 segundos por defecto): si el
/// navegador corta una renovación justo después de que el servidor rotó el token, se queda con la cookie vieja y su
/// renovación siguiente parecería un robo. Por eso, un token presentado otra vez se toma como una respuesta perdida, y
/// no como un robo, cuando se cumplen las tres cosas: lo revocó una rotación (tiene <c>ReplacedBy</c>), eso pasó hace
/// menos que el período de gracia, y su reemplazo sigue sin usar (vigente, nunca rotado). Entonces se revoca ese
/// reemplazo, que el navegador nunca recibió, y se entrega un par nuevo de la misma sesión (el mismo usuario; la
/// empresa activa se elige igual que en cualquier renovación). En cualquier otro caso (fuera del período, reemplazo ya
/// usado, token cerrado por un logout o por una revocación de todas las sesiones) sigue la respuesta de robo.
/// </para>
/// <para>
/// Dos solicitudes simultáneas con el mismo token viejo no reciben las dos un par vivo: las dos intentan reclamar el
/// mismo reemplazo con <see cref="IRefreshTokenStore.RevokeAsync"/>, igual que una rotación normal reclama el token
/// presentado. Si una ya lo revocó y confirmó, la otra lo ve revocado y recibe <see cref="SessionIssuer.AlreadyUsed"/>
/// (respuesta de robo); si lo leyeron a la vez, la versión de fila (<c>row_version</c>) hace fallar el segundo guardado
/// y esa solicitud responde 409. Si llegan una después de la otra, la segunda rota el reemplazo que recibió la primera,
/// que deja de servir: en ningún caso quedan dos tokens de renovación vivos. Los tokens de acceso ya entregados valen
/// hasta vencer, como en cualquier rotación.
/// </para>
/// <para>
/// Costo aceptado: quien robe un token y lo presente dentro del período de gracia, antes de que el dueño use el
/// reemplazo, obtiene un par y deja al dueño con un token revocado. Cuando el dueño lo presente después del período, o
/// con el reemplazo ya usado, se activa la respuesta de robo y se cierran todas las sesiones, la del atacante incluida.
/// </para>
/// </remarks>
internal sealed class RefreshTokenHandler(
    IRefreshTokenStore refreshStore,
    IRefreshTokenFactory refreshTokens,
    IUserStore users,
    IAccessTokenIssuer accessTokens,
    IClock clock,
    ISessionPolicy policy,
    ILogger<RefreshTokenHandler> logger) : IHandler<RefreshTokenCommand, SessionTokens>
{
    private readonly SessionIssuer _sessions = new(users, accessTokens, refreshTokens, refreshStore);

    public async Task<Result<SessionTokens>> HandleAsync(RefreshTokenCommand request, CancellationToken ct)
    {
        var presented = await refreshStore.FindByHashAsync(refreshTokens.Hash(request.RefreshToken), ct);
        if (presented is null)
        {
            return IdentityErrors.InvalidRefreshToken;
        }

        var now = clock.UtcNow;
        // El token que esta renovación reclama: el presentado o, tras una respuesta perdida, su reemplazo sin usar.
        var claimed = presented;
        if (presented.RevokedAt is not null)
        {
            if (await FindUnusedReplacementAsync(presented, now) is not { } lost)
            {
                return await RevokeChainAsync(presented.UserId, now);
            }

            claimed = lost;
        }
        else if (presented.ExpiresAt <= now)
        {
            return IdentityErrors.InvalidRefreshToken;
        }

        var user = await users.FindByIdAsync(presented.UserId, ct);
        if (user is null || !user.IsActive || user.LockoutEnd > now)
        {
            return IdentityErrors.InvalidRefreshToken;
        }

        var issued = await _sessions.IssueAsync(user, request.CompanyId, request.ClientIp, request.UserAgent, claimed.Id, now, ct);
        if (!issued.IsSuccess && issued.Error == SessionIssuer.AlreadyUsed)
        {
            // Otra solicitud usó este mismo token un instante antes: se trata igual que un token robado y reusado.
            return await RevokeChainAsync(presented.UserId, now);
        }

        if (issued.IsSuccess && claimed != presented)
        {
            logger.LogInformation(
                "Refresh token {RefreshTokenId} of user {UserId} was presented again {Seconds:0.#} s after its rotation, with its replacement {ReplacementId} unused; treated as a lost response.",
                presented.Id, presented.UserId, (now - presented.RevokedAt!.Value).TotalSeconds, claimed.Id);
        }

        return issued;
    }

    /// <summary>
    /// El reemplazo de un token ya rotado si la renovación que lo creó parece una respuesta perdida: el token lo revocó
    /// una rotación hace menos que el período de gracia, y el reemplazo sigue vigente (sin revocar ni vencer, así que
    /// nunca se rotó). Si no, <see langword="null"/>: es un reúso.
    /// </summary>
    /// <remarks>
    /// La lectura no usa la cancelación de la petición sino el plazo propio de las revocaciones: de ella depende la
    /// respuesta de robo, que debe terminar aunque el navegador corte la petición.
    /// </remarks>
    private async Task<RefreshTokenRecord?> FindUnusedReplacementAsync(RefreshTokenRecord presented, DateTimeOffset now)
    {
        if (presented is not { ReplacedBy: { } replacementId, RevokedAt: { } rotatedAt }
            || !SessionRotation.WithinGrace(rotatedAt, now, policy.RotationGracePeriod))
        {
            return null;
        }

        var replacement = await SessionRevocation.WithinDeadlineAsync(
            policy.RevocationTimeout, deadline => refreshStore.FindByIdAsync(replacementId, deadline));
        return replacement is { RevokedAt: null } && replacement.ExpiresAt > now && replacement.UserId == presented.UserId
            ? replacement
            : null;
    }

    /// <summary>
    /// Respuesta a un token robado y reusado: revoca todas las sesiones del usuario. No usa la cancelación de la
    /// petición: si la pestaña del usuario se recarga o se cierra justo en ese momento, la sesión del atacante seguiría
    /// viva. Se acota con su propio plazo (<see cref="ISessionPolicy.RevocationTimeout"/>); si se cumple, la API
    /// responde 503 <c>identity.store_unavailable</c> en lugar de un 500 (<see cref="SessionRevocation"/>).
    /// </summary>
    private async Task<Result<SessionTokens>> RevokeChainAsync(Guid userId, DateTimeOffset now)
    {
        await SessionRevocation.WithinDeadlineAsync(
            policy.RevocationTimeout, deadline => refreshStore.RevokeAllActiveForUserAsync(userId, now, deadline));
        logger.LogWarning("A revoked refresh token was presented again; revoked every active refresh token of user {UserId}.", userId);
        return IdentityErrors.InvalidRefreshToken;
    }
}
