using System.Text.Json.Serialization;
using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Api.OpenApi;
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
            .WithTags(ApiTags.Identity)
            .ExcludeFromDescription();

        var auth = endpoints.MapGroup("/api/v1/auth")
            .WithTags(ApiTags.Identity)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingExtensions.LoginPolicy)
            .AddEndpointFilter(NoStore);
        auth.MapPost("/login", LoginAsync)
            .Produces<SessionTokens>()
            .WithSummary("Iniciar sesión en el panel")
            .WithDescription(
                "Recibe `email` y `password` (y, si quiere, `company_id`, la empresa con la que va a trabajar) y entrega un " +
                "`access_token` (JWT de vida corta) y un `refresh_token`. El `access_token` se usa como credencial Bearer. " +
                "Un correo desconocido, una contraseña equivocada o un usuario inactivo o bloqueado responden el mismo 401 " +
                "(`identity.invalid_credentials`); una empresa a la que el usuario no pertenece responde 403 " +
                "(`identity.company_not_allowed`). Tiene un límite estricto de intentos por minuto y por IP (429).");
        auth.MapPost("/refresh", RefreshAsync)
            .Produces<SessionTokens>()
            .WithSummary("Renovar la sesión")
            .WithDescription(
                "Cambia un `refresh_token` vigente por un `access_token` y un `refresh_token` nuevos; el usado queda revocado. " +
                "Con `company_id` cambia la empresa activa del token. Un token vencido, revocado o desconocido responde 401 " +
                "(`identity.invalid_refresh_token`); presentar uno ya revocado revoca todas las sesiones del usuario. Comparte el " +
                "límite de intentos del inicio de sesión.");

        var apiKeys = endpoints.MapGroup("/api/v1/api-keys").WithTags(ApiTags.Identity).RequireAuthorization(IdentityPolicies.ManageCredentials);
        apiKeys.MapPost("/", CreateApiKeyAsync).AddEndpointFilter(NoStore)
            .Produces<CreatedApiKey>(StatusCodes.Status201Created)
            .WithSummary("Crear una API key")
            .WithDescription(
                "Crea una llave de acceso para una integración, con `name`, `scopes` y, si quiere, `expires_at`. Con una " +
                "credencial de empresa la llave es de esa empresa y solo puede llevar alcances que la credencial ya tiene " +
                "(nunca `platform:*`); con una credencial de plataforma es una llave de plataforma y solo lleva `platform:*`. " +
                "El texto completo de la llave (`key`) se muestra solo en esta respuesta. Alcances no permitidos: 403 " +
                "(`api_key.scope_not_allowed`).");
        apiKeys.MapDelete("/{id:guid}", RevokeApiKeyAsync)
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Revocar una API key")
            .WithDescription(
                "Revoca la llave con ese `id`; revocarla otra vez no cambia nada. Una empresa solo revoca sus propias llaves " +
                "(una de otra empresa responde 404, `api_key.not_found`); la plataforma puede revocar cualquiera. Las demás " +
                "instancias de la API dejan de aceptarla en máximo 60 segundos.");

        endpoints.MapGroup("/api/v1/users").WithTags(ApiTags.Identity).RequireAuthorization(IdentityPolicies.ManageCredentials)
            .MapPost("/", CreateUserAsync)
            .Produces<CreatedUser>(StatusCodes.Status201Created)
            .WithSummary("Crear un usuario del panel")
            .WithDescription(
                "Crea un usuario con `email`, `password` (al menos 10 caracteres), `name`, `role` y, si quiere, `phone`. El dueño (`owner`) de una " +
                "empresa, con su sesión del panel, crea usuarios de su empresa con rol `owner` o `seller`; la plataforma " +
                "crea administradores de plataforma (`platform_admin`). Las API keys de empresa no crean usuarios. Correo " +
                "repetido: 409 (`user.email_taken`); rol no permitido: 403 (`user.role_not_allowed`).");
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
