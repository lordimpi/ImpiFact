using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Infrastructure.Identity;
using ImpiFact.Infrastructure.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace ImpiFact.Api.Identity;

/// <summary>Nombres de los esquemas de autenticación.</summary>
public static class AuthenticationSchemes
{
    /// <summary>Esquema por defecto: decide entre API key y JWT según el token (ADR-12).</summary>
    public const string Bearer = "Bearer";

    public const string ApiKey = "ApiKey";

    /// <summary>JWT (JSON Web Token) de los usuarios del panel.</summary>
    public const string Jwt = "Jwt";
}

internal static class BearerAuthenticationExtensions
{
    /// <summary>Tolerancia de reloj entre servidores al validar el vencimiento de un JWT.</summary>
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Un solo encabezado <c>Authorization: Bearer &lt;token&gt;</c> para todos (ADR-12, así el Postman heredado
    /// funciona sin cambios): un esquema de política (un esquema que no autentica por sí mismo, solo reenvía) mira
    /// el token; si empieza con el prefijo de las API keys (<c>apk_</c>) lo atiende el esquema de API key, y si no,
    /// el de JWT. La firma del JWT es ES256 (ECDSA P-256 con SHA-256) con las llaves de <see cref="JwtSigningKeys"/>.
    /// </summary>
    public static IServiceCollection AddBearerAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(AuthenticationSchemes.Bearer)
            .AddPolicyScheme(AuthenticationSchemes.Bearer, "API key or JWT", options =>
                options.ForwardDefaultSelector = SelectScheme)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(AuthenticationSchemes.ApiKey, null)
            .AddJwtBearer(AuthenticationSchemes.Jwt);

        services.AddOptions<JwtBearerOptions>(AuthenticationSchemes.Jwt)
            .Configure<JwtSigningKeys, IOptionsService<JwtOptions>>((options, keys, jwt) =>
            {
                // Los claims se leen con sus nombres cortos (sub, role, company_id), sin traducirlos a los de Microsoft.
                options.MapInboundClaims = false;
                // No se explica al cliente por qué falló el token (vencido, firma, emisor…): solo 401.
                options.IncludeErrorDetails = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKeys = keys.ValidationKeys,
                    ValidAlgorithms = [JwtSigningKeys.Algorithm],
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = ClockSkew,
                    NameClaimType = IdentityClaimTypes.Subject,
                    RoleClaimType = IdentityClaimTypes.Role,
                    AuthenticationType = AuthenticationSchemes.Jwt,
                };
                options.Events = new JwtBearerEvents { OnTokenValidated = RejectMalformedIdentifiers };
            });
        return services;
    }

    /// <summary>
    /// Un token con la firma correcta pero con <c>sub</c> ausente o que no es un GUID (identificador único global), o
    /// con un <c>company_id</c> que no es un GUID (o repetido), no autentica. Si autenticara, la solicitud quedaría
    /// «autenticada» con un contexto de tenant anónimo y pasaría las políticas que solo exigen estar autenticado.
    /// </summary>
    private static Task RejectMalformedIdentifiers(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var subjects = principal?.FindAll(IdentityClaimTypes.Subject).ToList() ?? [];
        var companies = principal?.FindAll(IdentityClaimTypes.CompanyId).ToList() ?? [];
        if (subjects is not [{ } subject] || !Guid.TryParse(subject.Value, out _)
            || companies.Count > 1 || companies.Any(company => !Guid.TryParse(company.Value, out _)))
        {
            context.Fail("The token subject or company is not a valid identifier.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Políticas por alcance de la API key (Arquitectura detallada, sección 9.2). Toda ruta exige estar autenticado
    /// salvo las marcadas como anónimas. <c>platform:*</c> además exige que la credencial no sea de ninguna empresa:
    /// una llave de empresa nunca administra la plataforma aunque alguien le haya puesto ese alcance.
    /// </summary>
    public static IServiceCollection AddScopeAuthorization(this IServiceCollection services)
    {
        var builder = services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        foreach (var scope in new[] { ApiScopes.DocumentsWrite, ApiScopes.DocumentsRead, ApiScopes.ConfigWrite })
        {
            builder.AddPolicy(scope, policy => policy.RequireAuthenticatedUser().RequireClaim(IdentityClaimTypes.Scope, scope));
        }

        builder.AddPolicy(ApiScopes.Platform, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(IdentityClaimTypes.Scope, ApiScopes.Platform)
            .RequireAssertion(context => !context.User.HasClaim(claim => claim.Type == IdentityClaimTypes.CompanyId)));
        return services;
    }

    private static string SelectScheme(HttpContext context) =>
        BearerToken.TryRead(context.Request, out var token)
        && context.RequestServices.GetRequiredService<ApiKeyFormat>().IsApiKey(token)
            ? AuthenticationSchemes.ApiKey
            : AuthenticationSchemes.Jwt;
}
