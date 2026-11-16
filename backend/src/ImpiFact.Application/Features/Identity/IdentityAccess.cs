using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Identity;

namespace ImpiFact.Application.Features.Identity;

/// <summary>
/// Reglas de quién administra credenciales, compartidas por los casos de uso de Identity. Las políticas de la API ya
/// filtran por alcance; aquí se confirma contra la base, por si un token se emitió antes de que cambiaran los roles.
/// </summary>
internal static class IdentityAccess
{
    /// <summary>Correo como se guarda y se busca: sin espacios alrededor y en minúsculas.</summary>
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>
    /// Actúa por la plataforma: una API key de plataforma, o un usuario del panel sin empresa activa que tiene el
    /// rol <c>platform_admin</c>.
    /// </summary>
    public static async Task<bool> IsPlatformAsync(ITenantContext tenant, IUserStore users, CancellationToken ct) =>
        tenant.Actor == ActorType.Platform
        || (tenant is { Actor: ActorType.User, CompanyId: null, UserId: { } userId }
            && (await users.GetRolesAsync(userId, ct)).PlatformRoles.Contains(RoleNames.PlatformAdmin));

    /// <summary>Un usuario del panel con el rol <c>owner</c> en la empresa activa.</summary>
    public static async Task<bool> IsCompanyOwnerAsync(ITenantContext tenant, IUserStore users, CancellationToken ct) =>
        tenant is { Actor: ActorType.User, CompanyId: { } companyId, UserId: { } userId }
        && (await users.GetRolesAsync(userId, ct)).RolesIn(companyId).Contains(RoleNames.Owner);
}
