using System.Text.Json.Serialization;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Domain.Companies;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Features.Companies.Activation;

/// <summary>
/// <c>POST /api/v1/platform/companies/{nit}/deactivate</c>: the platform deactivates a company (decision of 22-nov, L17).
/// It replaces the PHP <c>delete/company/{nit}/{dv}</c>, which deleted the company with everything it had and let any
/// signed-in user delete any company (php-rules 5.2, item 6): ImpiFact never deletes a company.
/// </summary>
public sealed record DeactivateCompanyCommand(string Nit) : ICommand, ICompanyOptional;

/// <summary><c>POST /api/v1/platform/companies/{nit}/reactivate</c>: the platform lets a deactivated company work again.</summary>
public sealed record ReactivateCompanyCommand(string Nit) : ICommand, ICompanyOptional;

/// <summary>The company after the change, with what the change did.</summary>
public sealed record CompanyActivationResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>Razón social.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("nit")]
    public string Nit { get; init; } = "";

    [JsonPropertyName("dv")]
    public string Dv { get; init; } = "";

    [JsonPropertyName("is_active")]
    public bool IsActive { get; init; }

    /// <summary>False when the company was already in that state: nothing was done.</summary>
    [JsonPropertyName("changed")]
    public bool Changed { get; init; }

    /// <summary>API keys of the company revoked by this change; always 0 when reactivating.</summary>
    [JsonPropertyName("revoked_api_keys")]
    public int RevokedApiKeys { get; init; }

    /// <summary>Users of the company whose panel sessions this change closed; always 0 when reactivating.</summary>
    [JsonPropertyName("signed_out_users")]
    public int SignedOutUsers { get; init; }

    internal static CompanyActivationResponse From(Company company, bool changed, int revokedApiKeys = 0, int signedOutUsers = 0) => new()
    {
        Id = company.Id,
        Name = company.Name,
        Nit = company.IdentificationNumber,
        Dv = company.Dv,
        IsActive = company.IsActive,
        Changed = changed,
        RevokedApiKeys = revokedApiKeys,
        SignedOutUsers = signedOutUsers,
    };
}

/// <summary>Expected errors of deactivating and reactivating a company.</summary>
public static class CompanyActivationErrors
{
    /// <summary>The same code as the other platform-only operations of the module.</summary>
    public static readonly Error PlatformOnly = new(
        CompanyErrors.PlatformOnly.Code,
        "Solo el administrador de la plataforma puede desactivar o reactivar empresas.",
        ErrorType.Forbidden);

    public static readonly Error NotFound = new("company.not_found", "No existe una empresa con ese NIT.", ErrorType.NotFound);
}

/// <summary>
/// Deactivates the company in one transaction: <c>companies.is_active</c> becomes false, every API key of the company
/// still unrevoked is revoked for good (<see cref="IApiKeyStore.RevokeAllActiveForCompanyAsync"/>), and every refresh
/// token of its users is revoked with the same port that deactivating a membership uses
/// (<see cref="IRefreshTokenStore.RevokeAllActiveForUserWithChangesAsync"/>): if the save fails, nothing is revoked.
/// Refresh tokens do not record a company, so those users sign in again everywhere; only this company is refused
/// afterwards (the selector shows it disabled). The acting user keeps their own session. Access tokens already issued
/// keep reading until they expire (15 minutes at most), but issuing checks the company in the database and is refused at
/// once (<c>company.inactive</c>), and so are the owner operations. Documents, files and memberships are untouched.
/// Deactivating a company that is already inactive answers 200 with <c>changed</c> false and does nothing.
/// </summary>
internal sealed class DeactivateCompanyHandler(
    ITenantContext tenant,
    ICompanyRepository companies,
    IApiKeyStore apiKeys,
    IApiKeyCacheInvalidator apiKeyCache,
    ICompanyMemberStore members,
    IRefreshTokenStore refreshTokens,
    IClock clock,
    ILogger<DeactivateCompanyHandler> logger) : IHandler<DeactivateCompanyCommand, CompanyActivationResponse>
{
    public async Task<Result<CompanyActivationResponse>> HandleAsync(DeactivateCompanyCommand request, CancellationToken ct)
    {
        if (!CompanyAccess.IsPlatform(tenant))
        {
            return CompanyActivationErrors.PlatformOnly;
        }

        if (await companies.FindByNitAsync(request.Nit, ct) is not { } company)
        {
            return CompanyActivationErrors.NotFound;
        }

        if (!company.IsActive)
        {
            return CompanyActivationResponse.From(company, changed: false);
        }

        var now = clock.UtcNow;
        company.IsActive = false;
        var revokedKeys = await apiKeys.RevokeAllActiveForCompanyAsync(company.Id, now, ct);
        var users = (await members.ListActiveUserIdsAsync(company.Id, ct)).Where(userId => userId != tenant.UserId).ToList();
        foreach (var userId in users)
        {
            await refreshTokens.RevokeAllActiveForUserWithChangesAsync(userId, now, ct);
        }

        // Like revoking one key: this instance stops accepting them at once, the others within the cache lifetime.
        foreach (var keyHash in revokedKeys)
        {
            await apiKeyCache.InvalidateAsync(keyHash, ct);
        }

        logger.LogInformation(
            "Company {CompanyId} deactivated by {Actor} {ActorUserId}: {RevokedApiKeys} API keys revoked, {SignedOutUsers} users signed out.",
            company.Id, tenant.Actor, tenant.UserId, revokedKeys.Count, users.Count);
        return CompanyActivationResponse.From(company, changed: true, revokedKeys.Count, users.Count);
    }
}

/// <summary>
/// Reactivates the company: <c>companies.is_active</c> becomes true and its users can select it again. Nothing else is
/// restored: the API keys revoked when it was deactivated stay revoked (an owner creates new ones) and closed sessions
/// stay closed. Reactivating a company that is already active answers 200 with <c>changed</c> false and does nothing.
/// </summary>
internal sealed class ReactivateCompanyHandler(ITenantContext tenant, ICompanyRepository companies, ILogger<ReactivateCompanyHandler> logger)
    : IHandler<ReactivateCompanyCommand, CompanyActivationResponse>
{
    public async Task<Result<CompanyActivationResponse>> HandleAsync(ReactivateCompanyCommand request, CancellationToken ct)
    {
        if (!CompanyAccess.IsPlatform(tenant))
        {
            return CompanyActivationErrors.PlatformOnly;
        }

        if (await companies.FindByNitAsync(request.Nit, ct) is not { } company)
        {
            return CompanyActivationErrors.NotFound;
        }

        if (company.IsActive)
        {
            return CompanyActivationResponse.From(company, changed: false);
        }

        company.IsActive = true;
        logger.LogInformation("Company {CompanyId} reactivated by {Actor} {ActorUserId}.", company.Id, tenant.Actor, tenant.UserId);
        return CompanyActivationResponse.From(company, changed: true);
    }
}
