using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Identity;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>
/// Adaptador de <see cref="IUserStore"/> sobre <c>users</c>, <c>roles</c>, <c>user_roles</c> y
/// <c>user_companies</c> con EF Core (Entity Framework). Los roles se identifican por su nombre; sus llaves son las
/// fijas de <see cref="PlatformSeed"/>.
/// </summary>
internal sealed class EfUserStore(ImpiFactDbContext db) : IUserStore
{
    public Task<UserAccount?> FindByEmailAsync(string normalizedEmail, CancellationToken ct) =>
        CredentialStoreGuard.RunAsync(() => db.Set<User>().AsNoTracking()
            .Where(user => user.Email == normalizedEmail)
            .Select(user => new UserAccount(user.Id, user.Email, user.PasswordHash, user.IsActive, user.LockoutEnd))
            .FirstOrDefaultAsync(ct));

    public Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken ct) =>
        CredentialStoreGuard.RunAsync(() => db.Set<User>().AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new UserAccount(user.Id, user.Email, user.PasswordHash, user.IsActive, user.LockoutEnd))
            .FirstOrDefaultAsync(ct));

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct) =>
        CredentialStoreGuard.RunAsync(() => db.Set<User>().AnyAsync(user => user.Email == normalizedEmail, ct));

    public Task<UserRoles> GetRolesAsync(Guid userId, CancellationToken ct) =>
        CredentialStoreGuard.RunAsync(async () =>
        {
            var platformRoles = await db.Set<UserRole>().AsNoTracking()
                .Where(userRole => userRole.UserId == userId)
                .Join(db.Set<Role>(), userRole => userRole.RoleId, role => role.Id, (_, role) => role.Name)
                .OrderBy(name => name)
                .ToListAsync(ct);

            // A propósito sin el filtro de empresa: se necesitan todas las empresas del usuario antes de elegir una.
            var companies = await db.Set<UserCompany>().AsNoTracking()
                .IgnoreQueryFilters([QueryFilters.Tenant])
                .Where(link => link.UserId == userId)
                .Join(db.Set<Role>(), link => link.RoleId, role => role.Id, (link, role) => new { link.CompanyId, role.Name })
                .OrderBy(link => link.CompanyId).ThenBy(link => link.Name)
                .ToListAsync(ct);

            return new UserRoles(platformRoles, [.. companies.Select(link => new CompanyMembership(link.CompanyId, link.Name))]);
        });

    public Task UpdatePasswordHashAsync(Guid userId, string passwordHash, CancellationToken ct) =>
        CredentialStoreGuard.RunAsync(async () =>
        {
            var user = await db.Set<User>().FirstOrDefaultAsync(candidate => candidate.Id == userId, ct);
            if (user is not null)
            {
                user.PasswordHash = passwordHash;
            }
        });

    public Task AddAsync(NewUser user, CancellationToken ct)
    {
        db.Add(new User
        {
            Id = user.Id,
            Email = user.Email,
            PasswordHash = user.PasswordHash,
            Name = user.Name,
            Phone = user.Phone,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
        });
        foreach (var role in user.PlatformRoles.Distinct(StringComparer.Ordinal))
        {
            db.Add(new UserRole { UserId = user.Id, RoleId = RoleId(role) });
        }

        if (user.Company is { } company)
        {
            db.Add(new UserCompany { UserId = user.Id, CompanyId = company.CompanyId, RoleId = RoleId(company.Role) });
        }

        return Task.CompletedTask;
    }

    private static Guid RoleId(string name) =>
        PlatformSeed.Roles.FirstOrDefault(role => role.Name == name)?.Id
        ?? throw new ArgumentException($"Unknown role '{name}'.", nameof(name));
}
