namespace ImpiFact.Application.Abstractions.Identity;

/// <summary>
/// Port of the staff of one company: the users with the <c>owner</c> or <c>seller</c> role in <c>user_companies</c>.
/// Every method takes the company explicitly and only reads or writes rows of that company; the caller decides first
/// that the credential may act on it.
/// </summary>
public interface ICompanyMemberStore
{
    /// <returns>The id of the company with that NIT (without check digit), or <see langword="null"/> if none.</returns>
    Task<Guid?> FindCompanyIdAsync(string nit, CancellationToken ct);

    /// <returns>
    /// One entry per user with the <c>owner</c> or <c>seller</c> role in the company, active or not, sorted by name
    /// and then by email. Customers of the portal are not staff and are left out.
    /// </returns>
    Task<IReadOnlyList<CompanyMember>> ListAsync(Guid companyId, CancellationToken ct);

    /// <summary>
    /// Leaves the user with exactly <paramref name="role"/> as staff role in the company and sets whether the
    /// membership is active. It is saved when the use case transaction commits.
    /// When the change takes an active owner away (demotes or deactivates one), the store itself decides the
    /// last-owner rule on what it reads now, not on an earlier snapshot of the caller: with no other active owner it
    /// changes nothing and answers <see cref="CompanyMemberChange.LastOwner"/>. Otherwise the other active owners'
    /// rows are written too, so that if a simultaneous change removes one of them, one of the two saves fails with a
    /// concurrency conflict instead of leaving the company without an active owner.
    /// </summary>
    Task<CompanyMemberChange> ChangeAsync(Guid companyId, Guid userId, string role, bool isActive, CancellationToken ct);
}

/// <summary>Outcome of <see cref="ICompanyMemberStore.ChangeAsync"/>.</summary>
public enum CompanyMemberChange
{
    /// <summary>The change is applied, pending the commit of the use case transaction.</summary>
    Changed,

    /// <summary>The user is not staff of the company; nothing changed.</summary>
    NotFound,

    /// <summary>The change would leave the company without an active owner; nothing changed.</summary>
    LastOwner,
}

/// <summary>A staff member of a company, without any credential.</summary>
/// <param name="Role"><c>owner</c> or <c>seller</c>.</param>
/// <param name="IsActive">Whether the membership in this company is active.</param>
public sealed record CompanyMember(Guid UserId, string Email, string Name, string? Phone, string Role, bool IsActive);
