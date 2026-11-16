using ImpiFact.Domain.Identity;

namespace ImpiFact.Application.Abstractions.Identity;

/// <summary>
/// Alcances (scopes) de un usuario del panel según sus roles (Arquitectura detallada, sección 9.2). Se calculan al
/// emitir su JWT (JSON Web Token) y van en claims <c>scope</c>, así las políticas por alcance son las mismas para
/// usuarios y API keys:
/// <list type="bullet">
/// <item><c>owner</c>: <c>documents:read</c>, <c>documents:write</c> y <c>config:write</c> en la empresa activa.</item>
/// <item><c>seller</c>: <c>documents:read</c> y <c>documents:write</c> en la empresa activa.</item>
/// <item><c>customer</c>: <c>documents:read</c> en la empresa activa.</item>
/// <item><c>platform_admin</c>: <c>platform:*</c>, solo si el token no tiene empresa activa.</item>
/// <item><c>reseller</c> y cualquier otro rol: ninguno por ahora.</item>
/// </list>
/// Los roles de empresa no dan nada sin empresa activa, y <c>platform:*</c> nunca va junto a una empresa.
/// </summary>
public static class RoleScopes
{
    private static readonly string[] Order = [ApiScopes.DocumentsRead, ApiScopes.DocumentsWrite, ApiScopes.ConfigWrite, ApiScopes.Platform];

    private static readonly IReadOnlyDictionary<string, string[]> CompanyScopes = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [RoleNames.Owner] = [ApiScopes.DocumentsRead, ApiScopes.DocumentsWrite, ApiScopes.ConfigWrite],
        [RoleNames.Seller] = [ApiScopes.DocumentsRead, ApiScopes.DocumentsWrite],
        [RoleNames.Customer] = [ApiScopes.DocumentsRead],
    };


    /// <param name="roles">Roles que van en el token.</param>
    /// <param name="companyId">Empresa activa del token; nula si no tiene.</param>
    /// <returns>Los alcances sin repetir, siempre en el mismo orden.</returns>
    public static IReadOnlyList<string> For(IEnumerable<string> roles, Guid? companyId)
    {
        var scopes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in roles)
        {
            if (companyId is not null && CompanyScopes.TryGetValue(role, out var granted))
            {
                scopes.UnionWith(granted);
            }
            else if (companyId is null && role == RoleNames.PlatformAdmin)
            {
                scopes.Add(ApiScopes.Platform);
            }
        }

        return [.. Order.Where(scopes.Contains)];
    }
}
