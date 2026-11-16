using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Infrastructure.Options;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>
/// Tokens de renovación (refresh tokens): 32 bytes aleatorios en Base64 URL, sin datos dentro. Solo se guarda su
/// resumen SHA-256; vencen a los <see cref="JwtOptions.RefreshDays"/> días (14 por defecto).
/// </summary>
internal sealed class RefreshTokenFactory(IOptionsService<JwtOptions> options, IClock clock) : IRefreshTokenFactory
{
    public IssuedRefreshToken Create()
    {
        var value = CredentialHash.NewOpaqueValue();
        return new IssuedRefreshToken(value, Hash(value), clock.UtcNow.AddDays(options.Value.RefreshDays));
    }

    public string Hash(string token) => CredentialHash.Sha256Hex(token);
}
