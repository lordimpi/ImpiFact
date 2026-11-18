using System.Globalization;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Identity;
using ImpiFact.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>
/// Adaptador de <see cref="IUserCompanyReads"/> con EF Core (Entity Framework): une <c>user_companies</c>,
/// <c>roles</c> y <c>companies</c> en una sola consulta sin rastreo.
/// </summary>
internal sealed class EfUserCompanyReads(ImpiFactDbContext db) : IUserCompanyReads
{
    /// <summary>Orden de la razón social como la lee una persona en Colombia: sin distinguir mayúsculas y con tildes en su lugar.</summary>
    private static readonly StringComparer NameOrder = StringComparer.Create(CultureInfo.GetCultureInfo("es-CO"), CompareOptions.IgnoreCase);

    public Task<IReadOnlyList<AccessibleCompany>> ForUserAsync(Guid userId, CancellationToken ct) =>
        CredentialStoreGuard.RunAsync<IReadOnlyList<AccessibleCompany>>(async () =>
        {
            // A propósito sin el filtro de empresa: son todas las empresas del usuario, antes de elegir una.
            var rows = await db.Set<UserCompany>().AsNoTracking()
                .IgnoreQueryFilters([QueryFilters.Tenant])
                .Where(link => link.UserId == userId)
                .Join(db.Set<Role>(), link => link.RoleId, role => role.Id, (link, role) => new { link.CompanyId, Role = role.Name })
                .Join(
                    db.Set<Company>(),
                    link => link.CompanyId,
                    company => company.Id,
                    (link, company) => new { company.Id, company.Name, company.IdentificationNumber, company.Dv, company.IsActive, link.Role })
                .ToListAsync(ct);

            return [.. rows
                .GroupBy(row => row.Id)
                .Select(group =>
                {
                    var first = group.First();
                    return new AccessibleCompany(
                        first.Id, first.Name, first.IdentificationNumber, first.Dv, first.IsActive,
                        [.. group.Select(row => row.Role).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
                })
                .OrderBy(company => company.Name, NameOrder)
                .ThenBy(company => company.Id)];
        });

    public Task<AccessibleCompany?> ByIdAsync(Guid companyId, CancellationToken ct) =>
        CredentialStoreGuard.RunAsync(() => db.Set<Company>().AsNoTracking()
            .Where(company => company.Id == companyId)
            .Select(company => new AccessibleCompany(
                company.Id, company.Name, company.IdentificationNumber, company.Dv, company.IsActive, Array.Empty<string>()))
            .FirstOrDefaultAsync(ct));
}
