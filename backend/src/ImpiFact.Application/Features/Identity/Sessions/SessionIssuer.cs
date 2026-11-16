using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Identity.Sessions;

/// <summary>
/// Pasos comunes del inicio de sesión y la renovación: elegir la empresa activa, juntar los roles del token, emitir
/// el token de acceso y crear un token de renovación nuevo (solo se guarda su resumen).
/// </summary>
internal sealed class SessionIssuer(
    IUserStore users, IAccessTokenIssuer accessTokens, IRefreshTokenFactory refreshTokens, IRefreshTokenStore refreshStore)
{
    /// <summary>
    /// Empresa activa: la pedida, si el usuario pertenece a ella; si no se pide ninguna, la única empresa del
    /// usuario, salvo que tenga roles de plataforma (entonces ninguna, para que conserve <c>platform:*</c>). Los
    /// roles del token son los de plataforma más los de la empresa activa.
    /// </summary>
    /// <param name="replaces">Token de renovación que el nuevo reemplaza (rotación); nulo al iniciar sesión.</param>
    /// <summary>
    /// El token a rotar ya lo usó otra solicitud. No llega al cliente: <see cref="RefreshTokenHandler"/> lo trata como
    /// reuso y responde <see cref="IdentityErrors.InvalidRefreshToken"/>.
    /// </summary>
    internal static readonly Error AlreadyUsed = new("identity.refresh_already_used", "El token de renovación ya se usó.", ErrorType.Unauthorized);

    public async Task<Result<SessionTokens>> IssueAsync(
        Guid userId, Guid? requestedCompany, string? clientIp, string? userAgent, Guid? replaces, DateTimeOffset now, CancellationToken ct)
    {
        var roles = await users.GetRolesAsync(userId, ct);
        Guid? companyId;
        if (requestedCompany is { } requested)
        {
            if (!roles.CompanyIds.Contains(requested))
            {
                return IdentityErrors.CompanyNotAllowed;
            }

            companyId = requested;
        }
        else
        {
            companyId = roles.PlatformRoles.Count == 0 && roles.CompanyIds is [var only] ? only : null;
        }

        IReadOnlyList<string> tokenRoles = companyId is { } active
            ? [.. roles.PlatformRoles.Concat(roles.RolesIn(active)).Distinct(StringComparer.Ordinal)]
            : roles.PlatformRoles;

        // En una rotación, primero se reclama el token viejo: si otra solicitud ya lo usó, no se emite nada.
        var refreshId = Guid.CreateVersion7();
        if (replaces is { } oldId && !await refreshStore.RevokeAsync(oldId, now, refreshId, ct))
        {
            return AlreadyUsed;
        }

        var access = accessTokens.Issue(userId, tokenRoles, companyId);
        var refresh = refreshTokens.Create();
        await refreshStore.AddAsync(
            new RefreshTokenRecord(refreshId, userId, refresh.Hash, refresh.ExpiresAt, null, null, clientIp, userAgent), ct);

        return new SessionTokens(access.Value, access.ExpiresAt, refresh.Value, refresh.ExpiresAt, companyId, tokenRoles, roles.CompanyIds);
    }
}
