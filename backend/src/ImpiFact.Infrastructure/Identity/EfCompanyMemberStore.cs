using System.Globalization;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Identity;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>
/// Adapter of <see cref="ICompanyMemberStore"/> over <c>user_companies</c> and <c>users</c> with EF Core. The queries
/// skip the tenant filter on purpose (the platform acts without an active company) and filter by the company they
/// receive instead; the tenant write guard still checks every row that is saved.
/// </summary>
internal sealed class EfCompanyMemberStore(ImpiFactDbContext db, CredentialStoreGuard guard) : ICompanyMemberStore
{
    /// <summary>Name order as a person in Colombia reads it: case-insensitive, accents in place.</summary>
    private static readonly StringComparer NameOrder = StringComparer.Create(CultureInfo.GetCultureInfo("es-CO"), CompareOptions.IgnoreCase);

    private static readonly Guid OwnerRoleId = PlatformSeed.OwnerRoleId;

    private static readonly Guid SellerRoleId = PlatformSeed.SellerRoleId;

    public Task<Guid?> FindCompanyIdAsync(string nit, CancellationToken ct) =>
        guard.RunAsync(() => db.Set<Company>().AsNoTracking()
            .Where(company => company.IdentificationNumber == nit)
            .Select(company => (Guid?)company.Id)
            .FirstOrDefaultAsync(ct));

    public Task<IReadOnlyList<CompanyMember>> ListAsync(Guid companyId, CancellationToken ct) =>
        guard.RunAsync<IReadOnlyList<CompanyMember>>(async () =>
        {
            var rows = await db.Set<UserCompany>().AsNoTracking()
                .IgnoreQueryFilters([QueryFilters.Tenant])
                .Where(link => link.CompanyId == companyId && (link.RoleId == OwnerRoleId || link.RoleId == SellerRoleId))
                .Join(
                    db.Set<User>(),
                    link => link.UserId,
                    user => user.Id,
                    (link, user) => new { user.Id, user.Email, user.Name, user.Phone, link.RoleId, link.IsActive })
                .ToListAsync(ct);

            return [.. rows
                .GroupBy(row => row.Id)
                .Select(group =>
                {
                    // A user may hold both staff roles; the active rows decide, and owner wins over seller.
                    var first = group.First();
                    var isActive = group.Any(row => row.IsActive);
                    var deciding = group.Where(row => row.IsActive == isActive).ToList();
                    var role = deciding.Any(row => row.RoleId == OwnerRoleId) ? RoleNames.Owner : RoleNames.Seller;
                    return new CompanyMember(first.Id, first.Email, first.Name, first.Phone, role, isActive);
                })
                .OrderBy(member => member.Name, NameOrder)
                .ThenBy(member => member.Email, StringComparer.Ordinal)];
        });

    public Task<CompanyMemberChange> ChangeAsync(Guid companyId, Guid userId, string role, bool isActive, CancellationToken ct) =>
        guard.RunAsync(async () =>
        {
            var links = await db.Set<UserCompany>()
                .IgnoreQueryFilters([QueryFilters.Tenant])
                .Where(link => link.CompanyId == companyId && link.UserId == userId)
                .ToListAsync(ct);
            var staff = links.Where(link => link.RoleId == OwnerRoleId || link.RoleId == SellerRoleId).ToList();
            if (staff.Count == 0)
            {
                return CompanyMemberChange.NotFound;
            }

            var wasActiveOwner = staff.Any(link => link is { IsActive: true } && link.RoleId == OwnerRoleId);
            var roleId = role == RoleNames.Owner ? OwnerRoleId : SellerRoleId;
            if (wasActiveOwner && !(isActive && roleId == OwnerRoleId))
            {
                // The use case checked the last-owner rule on an earlier read; another request may have removed an
                // owner and committed since. Decide again on what the database holds now, before changing anything.
                // Writing the other active owners' rows bumps their row_version: if a simultaneous change removes one
                // of them, one of the two saves fails with a concurrency conflict instead of leaving no active owner.
                var otherOwners = await db.Set<UserCompany>()
                    .IgnoreQueryFilters([QueryFilters.Tenant])
                    .Where(link => link.CompanyId == companyId && link.UserId != userId && link.RoleId == OwnerRoleId && link.IsActive)
                    .ToListAsync(ct);
                if (otherOwners.Count == 0)
                {
                    return CompanyMemberChange.LastOwner;
                }

                foreach (var owner in otherOwners)
                {
                    db.Entry(owner).State = EntityState.Modified;
                }
            }

            var kept = staff.FirstOrDefault(link => link.RoleId == roleId) ?? staff[0];
            kept.RoleId = roleId;
            foreach (var extra in staff.Where(link => !ReferenceEquals(link, kept)))
            {
                db.Remove(extra);
            }

            // The whole membership in this company, the customer role of the portal included, follows the same state.
            foreach (var link in links)
            {
                link.IsActive = isActive;
            }

            return CompanyMemberChange.Changed;
        });

    public Task<IReadOnlyList<Guid>> ListActiveUserIdsAsync(Guid companyId, CancellationToken ct) =>
        guard.RunAsync<IReadOnlyList<Guid>>(async () => await db.Set<UserCompany>().AsNoTracking()
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(link => link.CompanyId == companyId && link.IsActive)
            .Select(link => link.UserId)
            .Distinct()
            .ToListAsync(ct));
}
