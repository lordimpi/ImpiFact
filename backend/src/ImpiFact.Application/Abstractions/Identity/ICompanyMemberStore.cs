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
    /// membership is active. When the change takes an active owner away, the other active owners' rows are written
    /// too, so that two simultaneous changes cannot both remove an owner (the second fails with a concurrency
    /// conflict). It is saved when the use case transaction commits.
    /// </summary>
    /// <returns><see langword="false"/> if the user is not staff of the company.</returns>
    Task<bool> ChangeAsync(Guid companyId, Guid userId, string role, bool isActive, CancellationToken ct);
}

/// <summary>A staff member of a company, without any credential.</summary>
/// <param name="Role"><c>owner</c> or <c>seller</c>.</param>
/// <param name="IsActive">Whether the membership in this company is active.</param>
public sealed record CompanyMember(Guid UserId, string Email, string Name, string? Phone, string Role, bool IsActive);
