using System.Security.Claims;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Infrastructure.Identity;
using ImpiFact.Infrastructure.Tenancy;

namespace ImpiFact.Api.Identity;

/// <summary>
/// Llena el contexto de tenant (tenant: cada empresa cliente con sus datos aislados) de la solicitud con lo que dejó
/// la autenticación. Va entre <c>Authentication</c> y <c>Authorization</c> (Arquitectura detallada, sección 9.1):
/// <list type="bullet">
/// <item>API key de empresa: empresa de la llave, actor <see cref="ActorType.ApiKey"/>.</item>
/// <item>API key sin empresa con alcance <c>platform:*</c>: sin empresa, actor <see cref="ActorType.Platform"/>.</item>
/// <item>JWT (JSON Web Token) del panel: usuario de <c>sub</c>, empresa activa de <c>company_id</c>, actor <see cref="ActorType.User"/>.</item>
/// </list>
/// Sin credenciales válidas el contexto queda anónimo.
/// </summary>
internal sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context, TenantContext tenant)
    {
        var user = context.User;
        if (user.Identity is { IsAuthenticated: true } identity)
        {
            var companyId = ReadGuid(user, IdentityClaimTypes.CompanyId);
            switch (identity.AuthenticationType)
            {
                case AuthenticationSchemes.ApiKey:
                    var actor = companyId is null && user.HasClaim(IdentityClaimTypes.Scope, ApiScopes.Platform)
                        ? ActorType.Platform
                        : ActorType.ApiKey;
                    tenant.Set(companyId, null, actor);
                    break;
                case AuthenticationSchemes.Jwt when ReadGuid(user, IdentityClaimTypes.Subject) is { } userId:
                    tenant.Set(companyId, userId, ActorType.User);
                    break;
            }
        }

        return next(context);
    }

    private static Guid? ReadGuid(ClaimsPrincipal user, string claimType) =>
        Guid.TryParse(user.FindFirst(claimType)?.Value, out var value) ? value : null;
}
