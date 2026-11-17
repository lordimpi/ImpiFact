using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;

namespace ImpiFact.Application.Features.Billing;

/// <summary>Quién administra los planes: la plataforma, nunca una empresa.</summary>
internal static class PlanAccess
{
    /// <summary>
    /// Una API key de plataforma, un proceso interno o un usuario <c>platform_admin</c> del panel, que trae
    /// <c>platform:*</c> en su JWT (token firmado del panel) y ninguna empresa activa (Arquitectura detallada, sección 9.2).
    /// </summary>
    public static bool IsPlatform(ITenantContext tenant) =>
        tenant.Actor is ActorType.Platform or ActorType.System
        || (tenant.Actor == ActorType.User && tenant.CompanyId is null && tenant.Scopes.Contains(ApiScopes.Platform));
}
