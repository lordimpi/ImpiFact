using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Domain.Companies;

namespace ImpiFact.Application.Features.Companies;

/// <summary>Resuelve la empresa sobre la que actúa una solicitud de configuración.</summary>
internal static class CompanyAccess
{
    /// <summary>
    /// Acts for the platform: a platform API key, an internal process or a <c>platform_admin</c> panel user, who carries
    /// <c>platform:*</c> in the signed JWT and no active company (Arquitectura detallada, section 9.2).
    /// </summary>
    public static bool IsPlatform(ITenantContext tenant) =>
        tenant.CompanyId is null
        && (tenant.Actor is ActorType.Platform or ActorType.System
            || (tenant.Actor == ActorType.User && tenant.Scopes.Contains(ApiScopes.Platform)));

    /// <summary>
    /// Devuelve la empresa de la credencial. Si la ruta trae un NIT y no es el de esa empresa, responde
    /// <see cref="CompanyErrors.ForeignNit"/> sin buscar el NIT: así la respuesta es la misma exista o no.
    /// </summary>
    public static async Task<Result<Company>> OwnCompanyAsync(
        ICompanyRepository companies, ITenantContext tenant, string? nit, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId)
        {
            return CompanyErrors.ForeignNit;
        }

        var company = await companies.FindByIdAsync(companyId, ct);
        if (company is null)
        {
            return CompanyErrors.NotFound;
        }

        return nit is not null && !string.Equals(nit, company.IdentificationNumber, StringComparison.Ordinal)
            ? CompanyErrors.ForeignNit
            : company;
    }
}
