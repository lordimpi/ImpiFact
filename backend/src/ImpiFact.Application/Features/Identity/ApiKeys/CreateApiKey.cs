using System.Text.Json.Serialization;
using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Identity.ApiKeys;

/// <summary>
/// Crea una API key (llave de acceso de una integración). Con empresa en el contexto es una llave de esa empresa;
/// sin empresa, una llave de plataforma, que solo puede crear quien actúa por la plataforma.
/// </summary>
/// <param name="Name">Nombre para reconocerla en el panel.</param>
/// <param name="Scopes">Alcances (ver <see cref="ApiScopes"/>).</param>
/// <param name="ExpiresAt">Vencimiento; nulo si no vence.</param>
public sealed record CreateApiKeyCommand(string Name, IReadOnlyList<string> Scopes, DateTimeOffset? ExpiresAt)
    : ICommand, ICompanyOptional;

/// <summary>
/// La llave creada. <see cref="Key"/> es el texto completo: se muestra esta única vez y nunca se guarda; después
/// solo queda <see cref="KeyPrefix"/> para reconocerla.
/// </summary>
public sealed record CreatedApiKey(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("key_prefix")] string KeyPrefix,
    [property: JsonPropertyName("scopes")] IReadOnlyList<string> Scopes,
    [property: JsonPropertyName("company_id")] Guid? CompanyId,
    [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt)
{
    /// <summary>No incluye el texto de la llave, para que un registro accidental del objeto no la filtre.</summary>
    public override string ToString() => $"CreatedApiKey {{ Id = {Id}, KeyPrefix = {KeyPrefix} }}";
}

internal sealed class CreateApiKeyValidator : AbstractValidator<CreateApiKeyCommand>
{
    public CreateApiKeyValidator(IClock clock)
    {
        RuleFor(command => command.Name)
            .NotEmpty().WithMessage("El nombre es obligatorio.")
            .MaximumLength(100).WithMessage("El nombre admite hasta 100 caracteres.")
            .OverridePropertyName("name");
        RuleFor(command => command.Scopes)
            .NotEmpty().WithMessage("Indique al menos un alcance.")
            .OverridePropertyName("scopes");
        RuleForEach(command => command.Scopes)
            .Must(scope => ApiScopes.All.Contains(scope)).WithMessage("El alcance «{PropertyValue}» no existe.")
            .OverridePropertyName("scopes");
        RuleFor(command => command.ExpiresAt)
            .Must(expiresAt => expiresAt is null || expiresAt > clock.UtcNow).WithMessage("La fecha de vencimiento debe ser futura.")
            .OverridePropertyName("expires_at");
    }
}

/// <summary>
/// Una llave de empresa nunca lleva <c>platform:*</c> y una de plataforma solo lleva <c>platform:*</c>. Si la pide
/// un usuario del panel para su empresa, se confirma en la base que sea <c>owner</c> de ella.
/// </summary>
internal sealed class CreateApiKeyHandler(
    ITenantContext tenant, IUserStore users, IApiKeyGenerator generator, IApiKeyStore keys)
    : IHandler<CreateApiKeyCommand, CreatedApiKey>
{
    public async Task<Result<CreatedApiKey>> HandleAsync(CreateApiKeyCommand request, CancellationToken ct)
    {
        var scopes = request.Scopes.Distinct(StringComparer.Ordinal).ToList();
        if (tenant.CompanyId is not null)
        {
            if (tenant.Actor == ActorType.User && !await IdentityAccess.IsCompanyOwnerAsync(tenant, users, ct))
            {
                return IdentityErrors.Forbidden;
            }

            if (scopes.Contains(ApiScopes.Platform))
            {
                return IdentityErrors.ApiKeyScopeNotAllowed;
            }
        }
        else
        {
            if (!await IdentityAccess.IsPlatformAsync(tenant, users, ct))
            {
                return IdentityErrors.Forbidden;
            }

            if (scopes.Any(scope => scope != ApiScopes.Platform))
            {
                return IdentityErrors.ApiKeyScopeNotAllowed;
            }
        }

        var generated = generator.Generate();
        var id = Guid.CreateVersion7();
        await keys.AddAsync(
            new NewApiKey(id, tenant.CompanyId, request.Name.Trim(), generated.DisplayPrefix, generated.Hash, scopes, request.ExpiresAt, tenant.UserId),
            ct);
        return new CreatedApiKey(id, request.Name.Trim(), generated.PlainText, generated.DisplayPrefix, scopes, tenant.CompanyId, request.ExpiresAt);
    }
}
