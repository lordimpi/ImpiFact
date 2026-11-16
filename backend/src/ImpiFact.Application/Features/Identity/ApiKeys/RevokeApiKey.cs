using System.Text.Json.Serialization;
using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Identity.ApiKeys;

/// <summary>Revoca una API key (pone <c>revoked_at</c>). Revocar otra vez la misma llave no cambia nada.</summary>
public sealed record RevokeApiKeyCommand(Guid ApiKeyId) : ICommand, ICompanyOptional;

public sealed record RevokedApiKey(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("revoked_at")] DateTimeOffset RevokedAt);

internal sealed class RevokeApiKeyValidator : AbstractValidator<RevokeApiKeyCommand>
{
    public RevokeApiKeyValidator() =>
        RuleFor(command => command.ApiKeyId)
            .NotEmpty().WithMessage("El id de la API key es obligatorio.")
            .OverridePropertyName("id");
}

/// <summary>
/// Con empresa en el contexto solo se revocan llaves de esa empresa; una llave de otra empresa responde «no
/// existe», sin revelar que existe. La plataforma puede revocar cualquier llave. Después limpia la caché de la
/// búsqueda de llaves de esta instancia (las demás dejan de aceptarla como máximo en 60 segundos).
/// </summary>
/// <remarks>
/// La caché se limpia antes de que el decorador de transacción confirme: una solicitud que llegue en ese instante
/// puede volver a guardar la llave sin revocar, y entonces esta instancia la acepta hasta 60 segundos más, igual que
/// las demás instancias.
/// </remarks>
internal sealed class RevokeApiKeyHandler(
    ITenantContext tenant, IUserStore users, IApiKeyStore keys, IApiKeyCacheInvalidator cache, IClock clock)
    : IHandler<RevokeApiKeyCommand, RevokedApiKey>
{
    public async Task<Result<RevokedApiKey>> HandleAsync(RevokeApiKeyCommand request, CancellationToken ct)
    {
        var isPlatform = tenant.CompanyId is null && await IdentityAccess.IsPlatformAsync(tenant, users, ct);
        if (tenant.CompanyId is null && !isPlatform)
        {
            return IdentityErrors.Forbidden;
        }

        if (tenant is { CompanyId: not null, Actor: ActorType.User } && !await IdentityAccess.IsCompanyOwnerAsync(tenant, users, ct))
        {
            return IdentityErrors.Forbidden;
        }

        var key = await keys.FindByIdAsync(request.ApiKeyId, ct);
        if (key is null || (!isPlatform && key.CompanyId != tenant.CompanyId))
        {
            return IdentityErrors.ApiKeyNotFound;
        }

        if (key.RevokedAt is { } alreadyRevoked)
        {
            return new RevokedApiKey(key.Id, alreadyRevoked);
        }

        var now = clock.UtcNow;
        await keys.RevokeAsync(key.Id, now, ct);
        await cache.InvalidateAsync(key.KeyHash, ct);
        return new RevokedApiKey(key.Id, now);
    }
}
