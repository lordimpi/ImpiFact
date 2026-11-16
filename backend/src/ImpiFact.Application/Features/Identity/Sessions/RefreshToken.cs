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
internal sealed class RefreshTokenHandler(
    IRefreshTokenStore refreshStore,
    IRefreshTokenFactory refreshTokens,
    IUserStore users,
    IAccessTokenIssuer accessTokens,
    IClock clock,
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
        if (presented.RevokedAt is not null)
        {
            return await RevokeChainAsync(presented.UserId, now, ct);
        }

        if (presented.ExpiresAt <= now)
        {
            return IdentityErrors.InvalidRefreshToken;
        }

        var user = await users.FindByIdAsync(presented.UserId, ct);
        if (user is null || !user.IsActive || user.LockoutEnd > now)
        {
            return IdentityErrors.InvalidRefreshToken;
        }

        var issued = await _sessions.IssueAsync(user.Id, request.CompanyId, request.ClientIp, request.UserAgent, presented.Id, now, ct);
        if (!issued.IsSuccess && issued.Error == SessionIssuer.AlreadyUsed)
        {
            // Otra solicitud usó este mismo token un instante antes: se trata igual que un token robado y reusado.
            return await RevokeChainAsync(presented.UserId, now, ct);
        }

        return issued;
    }

    private async Task<Result<SessionTokens>> RevokeChainAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        await refreshStore.RevokeAllActiveForUserAsync(userId, now, ct);
        logger.LogWarning("A revoked refresh token was presented again; revoked every active refresh token of user {UserId}.", userId);
        return IdentityErrors.InvalidRefreshToken;
    }
}
