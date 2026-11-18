using System.Text.Json.Serialization;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Identity.Me;

/// <summary>
/// Empresas a las que puede entrar quien llama, con razón social y NIT (número de identificación tributaria), para el
/// selector de empresa del panel (Arquitectura frontend, sección 16, punto 7). No exige empresa activa: se pide justo
/// antes de elegir una.
/// </summary>
public sealed record ListMyCompaniesQuery : ICompanyOptional;

/// <summary>Respuesta de <c>GET /api/v1/me/companies</c>.</summary>
/// <param name="Items">Las empresas ordenadas por razón social. Sin paginar: un usuario pertenece a pocas empresas.</param>
public sealed record MyCompanies([property: JsonPropertyName("items")] IReadOnlyList<MyCompany> Items);

/// <param name="Name">Razón social.</param>
/// <param name="Nit">NIT sin dígito de verificación.</param>
/// <param name="Dv">Dígito de verificación del NIT.</param>
/// <param name="Roles">Roles de quien llama en la empresa (<c>owner</c>, <c>seller</c>, <c>customer</c>); vacío para una API key.</param>
/// <param name="IsActive">Si la empresa está activa en la plataforma.</param>
public sealed record MyCompany(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("nit")] string Nit,
    [property: JsonPropertyName("dv")] string Dv,
    [property: JsonPropertyName("roles")] IReadOnlyList<string> Roles,
    [property: JsonPropertyName("is_active")] bool IsActive);

/// <summary>
/// Un usuario del panel recibe las empresas de <c>user_companies</c>, que son las únicas que el inicio de sesión y la
/// renovación le dejan activar (<see cref="Sessions.SessionIssuer"/>); por eso un <c>platform_admin</c> sin
/// pertenencias recibe la lista vacía: administra empresas por las rutas de plataforma, no entrando en ellas. Una API
/// key de empresa recibe solo su empresa, sin roles; una de plataforma, la lista vacía.
/// </summary>
internal sealed class ListMyCompaniesHandler(ITenantContext tenant, IUserCompanyReads companies) : IHandler<ListMyCompaniesQuery, MyCompanies>
{
    public async Task<Result<MyCompanies>> HandleAsync(ListMyCompaniesQuery request, CancellationToken ct)
    {
        IReadOnlyList<AccessibleCompany> found = tenant switch
        {
            { Actor: ActorType.User, UserId: { } userId } => await companies.ForUserAsync(userId, ct),
            { Actor: ActorType.ApiKey, CompanyId: { } companyId } =>
                await companies.ByIdAsync(companyId, ct) is { } own ? [own with { Roles = [] }] : [],
            _ => [],
        };

        return new MyCompanies([.. found.Select(company =>
            new MyCompany(company.Id, company.Name, company.Nit, company.Dv, company.Roles, company.IsActive))]);
    }
}
