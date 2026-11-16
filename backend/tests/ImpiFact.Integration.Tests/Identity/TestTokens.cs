using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ImpiFact.Integration.Tests.Identity;

/// <summary>
/// Firma JWT (JSON Web Token) propios para las pruebas: vencidos, con otra llave o con otro emisor. Usa ES256
/// (firma ECDSA con la curva P-256 y SHA-256), el mismo algoritmo de la API.
/// </summary>
internal static class TestTokens
{
    /// <summary>
    /// Identificador de la llave (kid) tal como lo calcula la API: la huella JWK (RFC 7638, un resumen SHA-256 de
    /// la parte pública de la llave) en Base64 URL.
    /// </summary>
    public static string KeyId(ECDsa key) =>
        Base64UrlEncoder.Encode(JsonWebKeyConverter.ConvertFromECDsaSecurityKey(new ECDsaSecurityKey(key)).ComputeJwkThumbprint());

    public static string Sign(
        ECDsa key,
        string? keyId = null,
        DateTimeOffset? issuedAt = null,
        TimeSpan? lifetime = null,
        string issuer = "impifact",
        string audience = "impifact-web",
        Guid? userId = null,
        Guid? companyId = null)
    {
        var iat = issuedAt ?? DateTimeOffset.UtcNow;
        var claims = new Dictionary<string, object>
        {
            ["sub"] = (userId ?? Guid.CreateVersion7()).ToString(),
            ["role"] = "owner",
        };
        if (companyId is not null)
        {
            claims["company_id"] = companyId.Value.ToString();
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = iat.UtcDateTime,
            NotBefore = iat.UtcDateTime,
            Expires = iat.Add(lifetime ?? TimeSpan.FromMinutes(15)).UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new ECDsaSecurityKey(key) { KeyId = keyId ?? KeyId(key) }, SecurityAlgorithms.EcdsaSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
