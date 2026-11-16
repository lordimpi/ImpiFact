using System.Text.Json.Serialization;
using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Features.Identity.ApiKeys;
using ImpiFact.Application.Features.Identity.Sessions;
using ImpiFact.Application.Features.Identity.Users;
using ImpiFact.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace ImpiFact.Api.Identity;

/// <summary>
/// Endpoints de Identity (Arquitectura detallada, secciones 3 y 9.3): inicio de sesión y renovación (anónimos, con el
/// límite estricto de inicio de sesión), API keys y usuarios (con la política
/// <see cref="IdentityPolicies.ManageCredentials"/>). Cada endpoint solo arma la solicitud, llama al caso de uso y
/// traduce el resultado.
/// </summary>
internal sealed class IdentityEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // JWKS (JSON Web Key Set): las llaves públicas que validan los JWT, para quien quiera validarlos por su
        // cuenta (Arquitectura detallada, sección 11). Solo partes públicas.
        endpoints.MapGet("/.well-known/jwks.json", (JwtSigningKeys keys) => TypedResults.Ok(new { keys = keys.PublicJsonWebKeys() }))
            .AllowAnonymous()
            .WithTags("Identity");

        var auth = endpoints.MapGroup("/api/v1/auth")
            .WithTags("Identity")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingExtensions.LoginPolicy)
            .AddEndpointFilter(NoStore);
        auth.MapPost("/login", LoginAsync);
        auth.MapPost("/refresh", RefreshAsync);

        var apiKeys = endpoints.MapGroup("/api/v1/api-keys").WithTags("Identity").RequireAuthorization(IdentityPolicies.ManageCredentials);
        apiKeys.MapPost("/", CreateApiKeyAsync).AddEndpointFilter(NoStore);
        apiKeys.MapDelete("/{id:guid}", RevokeApiKeyAsync);

        endpoints.MapGroup("/api/v1/users").WithTags("Identity").RequireAuthorization(IdentityPolicies.ManageCredentials)
            .MapPost("/", CreateUserAsync);
    }

    private static async Task<IResult> LoginAsync(
        LoginBody body, HttpContext context, [FromServices] IHandler<LoginCommand, SessionTokens> handler, CancellationToken ct) =>
        (await handler.HandleAsync(
            new LoginCommand(body.Email ?? "", body.Password ?? "", body.CompanyId, ClientIp(context), UserAgent(context)), ct))
        .ToHttpResult();

    private static async Task<IResult> RefreshAsync(
        RefreshBody body, HttpContext context, [FromServices] IHandler<RefreshTokenCommand, SessionTokens> handler, CancellationToken ct) =>
        (await handler.HandleAsync(
            new RefreshTokenCommand(body.RefreshToken ?? "", body.CompanyId, ClientIp(context), UserAgent(context)), ct))
        .ToHttpResult();

    private static async Task<IResult> CreateApiKeyAsync(
        CreateApiKeyBody body, [FromServices] IHandler<CreateApiKeyCommand, CreatedApiKey> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new CreateApiKeyCommand(body.Name ?? "", body.Scopes ?? [], body.ExpiresAt), ct))
        .ToHttpResult(created => TypedResults.Created($"/api/v1/api-keys/{created.Id}", created));

    private static async Task<IResult> RevokeApiKeyAsync(
        Guid id, [FromServices] IHandler<RevokeApiKeyCommand, RevokedApiKey> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new RevokeApiKeyCommand(id), ct)).ToHttpResult(_ => TypedResults.NoContent());

    private static async Task<IResult> CreateUserAsync(
        CreateUserBody body, [FromServices] IHandler<CreateUserCommand, CreatedUser> handler, CancellationToken ct) =>
        (await handler.HandleAsync(
            new CreateUserCommand(body.Email ?? "", body.Password ?? "", body.Name ?? "", body.Phone, body.Role ?? ""), ct))
        .ToHttpResult(created => TypedResults.Created($"/api/v1/users/{created.Id}", created));

    private static string? ClientIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString();

    private static string? UserAgent(HttpContext context) =>
        context.Request.Headers[HeaderNames.UserAgent] is { Count: > 0 } agent ? agent.ToString() : null;

    /// <summary>Las respuestas que traen credenciales no se guardan en cachés del navegador ni de proxies.</summary>
    private static async ValueTask<object?> NoStore(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        invocation.HttpContext.Response.Headers[HeaderNames.CacheControl] = "no-store";
        return await next(invocation);
    }

    // Cuerpos de las solicitudes. Todo es opcional aquí para que los validadores del caso de uso informen cada campo
    // que falta con su mensaje en español.

    internal sealed record LoginBody(
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("password")] string? Password,
        [property: JsonPropertyName("company_id")] Guid? CompanyId)
    {
        public override string ToString() => "LoginBody";
    }

    internal sealed record RefreshBody(
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("company_id")] Guid? CompanyId)
    {
        public override string ToString() => "RefreshBody";
    }

    internal sealed record CreateApiKeyBody(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("scopes")] string[]? Scopes,
        [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt);

    internal sealed record CreateUserBody(
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("password")] string? Password,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("phone")] string? Phone,
        [property: JsonPropertyName("role")] string? Role)
    {
        public override string ToString() => "CreateUserBody";
    }
}
