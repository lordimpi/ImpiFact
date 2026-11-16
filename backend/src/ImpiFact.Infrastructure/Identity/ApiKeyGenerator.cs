using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Infrastructure.Options;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>
/// Crea API keys con el formato <c>{prefijo}{43 caracteres aleatorios}</c>, por ejemplo <c>apk_Xb3…</c>. El
/// prefijo (<see cref="ApiKeyOptions.Prefix"/>) es lo que permite distinguir una API key de un JWT (JSON Web
/// Token) en el mismo encabezado Bearer (ADR-12).
/// </summary>
internal sealed class ApiKeyGenerator(IOptionsService<ApiKeyOptions> options) : IApiKeyGenerator
{
    /// <summary>Largo del prefijo que se guarda para mostrar (<c>api_keys.key_prefix varchar(12)</c>).</summary>
    public const int DisplayPrefixLength = 12;

    public GeneratedApiKey Generate()
    {
        var plainText = options.Value.Prefix + CredentialHash.NewOpaqueValue();
        return new GeneratedApiKey(plainText, plainText[..DisplayPrefixLength], CredentialHash.Sha256Hex(plainText));
    }
}
