using System.Text.Json.Serialization;
using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Features.Identity.Users;

/// <summary><c>GET /api/v1/companies/{nit}/users</c>: the staff (owners and sellers) of the company.</summary>
public sealed record ListCompanyUsersQuery(string Nit) : ICompanyOptional;

/// <summary>
/// <c>PATCH /api/v1/companies/{nit}/users/{userId}</c>: changes the role (<c>owner</c> or <c>seller</c>) and whether
/// the membership in that company is active. A field left out keeps its value.
/// </summary>
public sealed record UpdateCompanyUserCommand(string Nit, Guid UserId, string? Role, bool? IsActive) : ICommand, ICompanyOptional;

public sealed record CompanyUserList([property: JsonPropertyName("items")] IReadOnlyList<CompanyUser> Items);

/// <summary>A user of the company as the owner sees it: never a password hash, token or security stamp.</summary>
public sealed record CompanyUser(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("phone")] string? Phone,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("is_active")] bool IsActive)
{
    internal static CompanyUser From(CompanyMember member) =>
        new(member.UserId, member.Email, member.Name, member.Phone, member.Role, member.IsActive);
}

internal sealed class UpdateCompanyUserValidator : AbstractValidator<UpdateCompanyUserCommand>
{
    public UpdateCompanyUserValidator()
    {
        RuleFor(command => command.Role)
            .Must(role => role is null || CompanyUserRules.StaffRoles.Contains(role))
            .WithMessage("El rol debe ser «owner» (dueño) o «seller» (vendedor).")
            .OverridePropertyName("role");
        RuleFor(command => command)
            .Must(command => command.Role is not null || command.IsActive is not null)
            .WithMessage("Indique el rol (role), el estado (is_active) o ambos.")
            .OverridePropertyName("role");
    }
}

/// <summary>Rules of a membership change; pure, so the use case and its unit tests share them.</summary>
internal static class CompanyUserRules
{
    public static readonly string[] StaffRoles = [RoleNames.Owner, RoleNames.Seller];

    /// <param name="actingUserId">The panel user making the change; <see langword="null"/> for an API key or the platform key.</param>
    /// <param name="target">The membership as it is now.</param>
    /// <param name="activeOwners">Active owners of the company now, <paramref name="target"/> included.</param>
    /// <returns>The broken rule, or <see langword="null"/> if the change is allowed.</returns>
    public static Error? Check(Guid? actingUserId, CompanyMember target, string role, bool isActive, int activeOwners)
    {
        if (actingUserId == target.UserId)
        {
            if (target.IsActive && !isActive)
            {
                return IdentityErrors.CannotDeactivateSelf;
            }

            if (target.Role == RoleNames.Owner && role != RoleNames.Owner)
            {
                return IdentityErrors.CannotRemoveOwnOwnerRole;
            }
        }

        var wasActiveOwner = target is { IsActive: true, Role: RoleNames.Owner };
        var staysActiveOwner = isActive && role == RoleNames.Owner;
        return wasActiveOwner && !staysActiveOwner && activeOwners <= 1 ? IdentityErrors.LastOwner : null;
    }
}

/// <summary>
/// Decides on which company a request for its users acts, following the company routes: with a company credential the
/// <c>{nit}</c> must be that company's and, for a panel user, the caller must be one of its active owners; otherwise the
/// answer is <see cref="IdentityErrors.ForeignCompany"/>, the same whether the NIT exists or not. Acting for the
/// platform (platform key or <c>platform_admin</c> without an active company) any company can be named, and an unknown
/// NIT is <see cref="IdentityErrors.CompanyNotFound"/>.
/// </summary>
internal static class CompanyUserAccess
{
    public static async Task<Result<Guid>> ResolveAsync(
        ITenantContext tenant, IUserStore users, IUserCompanyReads companies, ICompanyMemberStore members, string nit, CancellationToken ct)
    {
        if (tenant.CompanyId is { } companyId)
        {
            var own = await companies.ByIdAsync(companyId, ct);
            var allowed = own is not null
                && string.Equals(own.Nit, nit, StringComparison.Ordinal)
                && (tenant.Actor != ActorType.User || await IdentityAccess.IsCompanyOwnerAsync(tenant, users, ct));
            return allowed ? companyId : IdentityErrors.ForeignCompany;
        }

        if (!await IdentityAccess.IsPlatformAsync(tenant, users, ct))
        {
            return IdentityErrors.Forbidden;
        }

        return await members.FindCompanyIdAsync(nit, ct) is { } target ? target : IdentityErrors.CompanyNotFound;
    }
}

internal sealed class ListCompanyUsersHandler(
    ITenantContext tenant, IUserStore users, IUserCompanyReads companies, ICompanyMemberStore members)
    : IHandler<ListCompanyUsersQuery, CompanyUserList>
{
    public async Task<Result<CompanyUserList>> HandleAsync(ListCompanyUsersQuery request, CancellationToken ct)
    {
        var company = await CompanyUserAccess.ResolveAsync(tenant, users, companies, members, request.Nit, ct);
        if (!company.IsSuccess)
        {
            return company.Error;
        }

        var list = await members.ListAsync(company.Value, ct);
        return new CompanyUserList([.. list.Select(CompanyUser.From)]);
    }
}

/// <summary>
/// Applies <see cref="CompanyUserRules"/> and saves the change. Deactivating a membership revokes every refresh token
/// of the user with the existing mechanism (<see cref="IRefreshTokenStore.RevokeAllActiveForUserAsync"/>, saved at
/// once): refresh tokens do not record a company, so the user signs in again everywhere, but only this membership is
/// refused afterwards. Access tokens already issued keep working until they expire (15 minutes at most), except for
/// the operations that check the role in the database.
/// </summary>
internal sealed class UpdateCompanyUserHandler(
    ITenantContext tenant,
    IUserStore users,
    IUserCompanyReads companies,
    ICompanyMemberStore members,
    IRefreshTokenStore refreshTokens,
    IClock clock,
    ILogger<UpdateCompanyUserHandler> logger) : IHandler<UpdateCompanyUserCommand, CompanyUser>
{
    public async Task<Result<CompanyUser>> HandleAsync(UpdateCompanyUserCommand request, CancellationToken ct)
    {
        var company = await CompanyUserAccess.ResolveAsync(tenant, users, companies, members, request.Nit, ct);
        if (!company.IsSuccess)
        {
            return company.Error;
        }

        var staff = await members.ListAsync(company.Value, ct);
        if (staff.FirstOrDefault(member => member.UserId == request.UserId) is not { } target)
        {
            return IdentityErrors.UserNotFound;
        }

        var role = request.Role ?? target.Role;
        var isActive = request.IsActive ?? target.IsActive;
        var actingUser = tenant.Actor == ActorType.User ? tenant.UserId : null;
        var activeOwners = staff.Count(member => member is { IsActive: true, Role: RoleNames.Owner });
        if (CompanyUserRules.Check(actingUser, target, role, isActive, activeOwners) is { } broken)
        {
            return broken;
        }

        if (!await members.ChangeAsync(company.Value, target.UserId, role, isActive, ct))
        {
            return IdentityErrors.UserNotFound;
        }

        if (target.IsActive && !isActive)
        {
            await refreshTokens.RevokeAllActiveForUserAsync(target.UserId, clock.UtcNow, ct);
        }

        logger.LogInformation(
            "Membership of user {UserId} in company {CompanyId} set to role {Role}, active {IsActive}, by {Actor} {ActorUserId}.",
            target.UserId, company.Value, role, isActive, tenant.Actor, tenant.UserId);
        return CompanyUser.From(target with { Role = role, IsActive = isActive });
    }
}
