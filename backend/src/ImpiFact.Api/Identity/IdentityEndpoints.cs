using System.Text.Json.Serialization;
using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Api.OpenApi;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Identity;
using ImpiFact.Application.Features.Identity.ApiKeys;
using ImpiFact.Application.Features.Identity.Sessions;
using ImpiFact.Application.Features.Identity.Users;
using ImpiFact.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace ImpiFact.Api.Identity;

/// <summary>
/// Endpoints de Identity (Arquitectura detallada, secciones 3 y 9.3): inicio de sesión, renovación y cierre de sesión
/// (anónimos, con el límite estricto de inicio de sesión; el panel usa la cookie de <see cref="PanelSession"/>), API
/// keys y usuarios (con la política <see cref="IdentityPolicies.ManageCredentials"/>). Cada endpoint solo arma la
/// solicitud, llama al caso de uso y traduce el resultado.
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
            .WithRequestExamples(ExampleBodies.Login)
            .WithSummary("Iniciar sesión en el panel")
            .WithDescription(
                "Recibe `email` y `password` (y, si quiere, `company_id`, la empresa con la que va a trabajar) y entrega un " +
                "`access_token` (JWT de vida corta) y un `refresh_token`. El `access_token` se usa como credencial Bearer. " +
                "Un correo desconocido, una contraseña equivocada o un usuario inactivo o bloqueado responden el mismo 401 " +
                "(`identity.invalid_credentials`); una empresa a la que el usuario no pertenece responde 403 " +
                "(`identity.company_not_allowed`). Tiene un límite estricto de intentos por minuto y por IP (429).\n\n" +
                PanelCookieNote);
        auth.MapPost("/refresh", RefreshAsync)
            .Produces<SessionTokens>()
            .WithRequestExamples(ExampleBodies.Refresh)
            .WithSummary("Renovar la sesión")
            .WithDescription(
                "Cambia un `refresh_token` vigente por un `access_token` y un `refresh_token` nuevos; el usado queda revocado. " +
                "Con `company_id` cambia la empresa activa del token. Un token vencido, revocado o desconocido responde 401 " +
                "(`identity.invalid_refresh_token`); presentar uno ya revocado revoca todas las sesiones del usuario. Comparte el " +
                "límite de intentos del inicio de sesión.\n\n" +
                PanelCookieNote + " Con `X-Client: panel` y sin `refresh_token` en el cuerpo, la renovación usa la cookie " +
                "(el cuerpo puede faltar o traer solo `company_id`) y la reemplaza por la nueva; si no hay cookie, o el token no " +
                "sirve, responde 401 y borra la cookie.");
        auth.MapPost("/logout", LogoutAsync)
            .Produces(StatusCodes.Status204NoContent)
            .WithRequestExamples(ExampleBodies.Logout)
            .WithSummary("Cerrar la sesión")
            .WithDescription(
                "Revoca el `refresh_token` de la sesión, del cuerpo o, para el panel, de la cookie: esa sesión ya no puede " +
                "renovarse. Las demás sesiones del usuario (otros navegadores) siguen vivas, y el `access_token` que ya se " +
                "entregó vale hasta su vencimiento (15 minutos como máximo). Responde 204 también si el token no existe o ya " +
                "estaba revocado, así se puede repetir sin error. Comparte el límite de intentos del inicio de sesión.\n\n" +
                PanelCookieNote + " Con `X-Client: panel`, además, borra la cookie.");

        var apiKeys = endpoints.MapGroup("/api/v1/api-keys").WithTags(ApiTags.Identity).RequireAuthorization(IdentityPolicies.ManageCredentials);
        apiKeys.MapPost("/", CreateApiKeyAsync).AddEndpointFilter(NoStore)
            .Produces<CreatedApiKey>(StatusCodes.Status201Created)
            .WithRequestExamples(ExampleBodies.ApiKey)
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
            .WithRequestExamples(ExampleBodies.User)
            .WithSummary("Crear un usuario del panel")
            .WithDescription(
                "Crea un usuario con `email`, `password` (al menos 10 caracteres), `name`, `role` y, si quiere, `phone`. El dueño (`owner`) de una " +
                "empresa, con su sesión del panel, crea usuarios de su empresa con rol `owner` o `seller`; la plataforma " +
                "crea administradores de plataforma (`platform_admin`). Las API keys de empresa no crean usuarios. Correo " +
                "repetido: 409 (`user.email_taken`); rol no permitido: 403 (`user.role_not_allowed`).");
    }

    /// <summary>Texto común de las rutas de sesión sobre la cookie del panel (<see cref="PanelSession"/>).</summary>
    private const string PanelCookieNote =
        "**Panel web:** con el encabezado `X-Client: panel`, el `refresh_token` no va en el cuerpo sino en la cookie " +
        "`" + PanelSession.CookieName + "` (`HttpOnly`, `Secure`, `SameSite=Strict`, solo para `" + PanelSession.CookiePath + "`, " +
        "con el vencimiento del token). Esa cookie solo se acepta con el mismo encabezado; sin él responde 403 " +
        "(`identity.client_header_required`), para que otra página no pueda usarla.";

    private static async Task<IResult> LoginAsync(
        LoginBody body,
        [FromHeader(Name = PanelSession.ClientHeader)] string? client,
        HttpContext context,
        [FromServices] IHandler<LoginCommand, SessionTokens> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(
            new LoginCommand(body.Email ?? "", body.Password ?? "", body.CompanyId, ClientIp(context), UserAgent(context)), ct);
        return PanelSession.IsPanel(client) ? result.ToHttpResult(tokens => PanelSession.Issue(context, tokens)) : result.ToHttpResult();
    }

    /// <summary>
    /// El token sale del cuerpo o, para el panel, de la cookie. Si el panel no tiene cookie, no hay sesión: 401, como un
    /// token que no sirve. Un 401 con la cookie la borra, para que el navegador no la siga enviando.
    /// </summary>
    private static async Task<IResult> RefreshAsync(
        RefreshBody? body,
        [FromHeader(Name = PanelSession.ClientHeader)] string? client,
        HttpContext context,
        [FromServices] IHandler<RefreshTokenCommand, SessionTokens> handler,
        CancellationToken ct)
    {
        var isPanel = PanelSession.IsPanel(client);
        var presented = PanelSession.ReadRefreshToken(context.Request, body?.RefreshToken, isPanel);
        if (!presented.IsSuccess)
        {
            return presented.Error.ToHttpResult();
        }

        if (isPanel && presented.Value.Value.Length == 0)
        {
            return IdentityErrors.InvalidRefreshToken.ToHttpResult();
        }

        var result = await handler.HandleAsync(
            new RefreshTokenCommand(presented.Value.Value, body?.CompanyId, ClientIp(context), UserAgent(context)), ct);
        if (!result.IsSuccess)
        {
            if (presented.Value.FromCookie && result.Error.Type == ErrorType.Unauthorized)
            {
                PanelSession.Clear(context);
            }

            return result.Error.ToHttpResult();
        }

        return isPanel ? PanelSession.Issue(context, result.Value) : result.ToHttpResult();
    }

    /// <summary>Sin token no hay nada que revocar: responde 204 sin llamar al caso de uso.</summary>
    private static async Task<IResult> LogoutAsync(
        LogoutBody? body,
        [FromHeader(Name = PanelSession.ClientHeader)] string? client,
        HttpContext context,
        [FromServices] IHandler<LogoutCommand, LoggedOut> handler,
        CancellationToken ct)
    {
        var isPanel = PanelSession.IsPanel(client);
        var presented = PanelSession.ReadRefreshToken(context.Request, body?.RefreshToken, isPanel);
        if (!presented.IsSuccess)
        {
            return presented.Error.ToHttpResult();
        }

        if (presented.Value.Value.Length > 0)
        {
            var result = await handler.HandleAsync(new LogoutCommand(presented.Value.Value), ct);
            if (!result.IsSuccess)
            {
                return result.Error.ToHttpResult();
            }
        }

        if (isPanel)
        {
            PanelSession.Clear(context);
        }

        return TypedResults.NoContent();
    }

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

    internal sealed record LogoutBody([property: JsonPropertyName("refresh_token")] string? RefreshToken)
    {
        public override string ToString() => "LogoutBody";
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
