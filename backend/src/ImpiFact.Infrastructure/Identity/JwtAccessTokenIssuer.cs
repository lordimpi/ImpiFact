using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Infrastructure.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>
/// Emite los JWT (JSON Web Token) de acceso del panel firmados con ES256 (ECDSA P-256 con SHA-256) y la llave
/// actual de <see cref="JwtSigningKeys"/>; el encabezado lleva su kid (identificador de llave). Claims: <c>sub</c>,
/// <c>role</c>, <c>company_id</c> (si hay empresa activa), <c>jti</c> (identificador único del token), emisor,
/// audiencia y fechas tomadas de <see cref="IClock"/>.
/// </summary>
internal sealed class JwtAccessTokenIssuer(JwtSigningKeys keys, IOptionsService<JwtOptions> options, IClock clock) : IAccessTokenIssuer
{
    private static readonly JsonWebTokenHandler Handler = new() { SetDefaultTimesOnTokenCreation = false };

    public AccessToken Issue(Guid userId, IReadOnlyCollection<string> roles, Guid? companyId)
    {
        var jwt = options.Value;
        // Las fechas de un JWT van en segundos enteros; se recorta aquí para que ExpiresAt diga lo mismo que el token.
        var now = clock.UtcNow;
        var issuedAt = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
        var expiresAt = issuedAt.AddMinutes(jwt.AccessMinutes);
        var claims = new Dictionary<string, object>
        {
            [IdentityClaimTypes.Subject] = userId.ToString(),
            [IdentityClaimTypes.Role] = roles.ToArray(),
            [JwtRegisteredClaimNames.Jti] = Guid.CreateVersion7().ToString(),
        };
        if (companyId is not null)
        {
            claims[IdentityClaimTypes.CompanyId] = companyId.Value.ToString();
        }

        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(keys.SigningKey, JwtSigningKeys.Algorithm),
        });
        return new AccessToken(token, expiresAt);
    }
}
