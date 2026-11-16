using System.Security.Claims;
using System.Text.Encodings.Web;
using ImpiFact.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace ImpiFact.Api.Identity;

/// <summary>
/// Esquema de autenticación de las API keys (llaves de acceso de las integraciones). Solo actúa si el token del
/// encabezado Bearer empieza con el prefijo de las llaves; crea los claims <c>api_key_id</c>, <c>company_id</c>
/// (si la llave es de una empresa) y un <c>scope</c> por cada alcance. El mensaje de fallo es el mismo para una
/// llave inexistente, revocada o vencida, para no dar pistas a quien prueba llaves.
/// </summary>
internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiKeyFormat format,
    ApiKeyAuthenticator authenticator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!BearerToken.TryRead(Request, out var token) || !format.IsApiKey(token))
        {
            return AuthenticateResult.NoResult();
        }

        var key = await authenticator.AuthenticateAsync(token, Context.RequestAborted);
        if (key is null)
        {
            return AuthenticateResult.Fail("Invalid, revoked or expired API key.");
        }

        var claims = new List<Claim> { new(IdentityClaimTypes.ApiKeyId, key.Id.ToString()) };
        if (key.CompanyId is { } companyId)
        {
            claims.Add(new Claim(IdentityClaimTypes.CompanyId, companyId.ToString()));
        }

        claims.AddRange(key.Scopes.Select(scope => new Claim(IdentityClaimTypes.Scope, scope)));
        var identity = new ClaimsIdentity(claims, Scheme.Name, IdentityClaimTypes.ApiKeyId, IdentityClaimTypes.Role);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer";
        return Task.CompletedTask;
    }
}
