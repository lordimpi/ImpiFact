using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Api.OpenApi;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Identity;
using ImpiFact.Application.Features.Identity.ApiKeys;
using ImpiFact.Application.Features.Identity.Me;
using ImpiFact.Application.Features.Identity.PasswordReset;
using ImpiFact.Application.Features.Identity.Sessions;
using ImpiFact.Application.Features.Identity.Users;
using ImpiFact.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi;

namespace ImpiFact.Api.Identity;

/// <summary>
/// Endpoints de Identity (Arquitectura detallada, secciones 3 y 9.3): inicio de sesión, renovación, cierre de sesión y
/// recuperación de contraseña (anónimos, con el límite estricto de inicio de sesión; el panel usa la cookie de
/// <see cref="PanelSession"/>), las empresas de quien llama (cualquier credencial), API keys y usuarios (con la política
/// <see cref="IdentityPolicies.ManageCredentials"/>). Cada endpoint solo arma la
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
            .WithSessionResponses()
            .WithRequestExamples(ExampleBodies.Login)
            .WithOperationId("login")
            .WithSummary("Iniciar sesión en el panel")
            .WithDescription(
                "Recibe `email` y `password` (y, si quiere, `company_id`, la empresa con la que va a trabajar) y entrega un " +
                "`access_token` (JWT de vida corta), un `refresh_token` y el `email` del usuario tal como está guardado. El " +
                "`access_token` se usa como credencial Bearer. " +
                "Un correo desconocido, una contraseña equivocada o un usuario inactivo o bloqueado responden el mismo 401 " +
                "(`identity.invalid_credentials`); una empresa a la que el usuario no pertenece, o donde su pertenencia está " +
                "desactivada, responde 403 " +
                "(`identity.company_not_allowed`). Tiene un límite estricto de intentos por minuto y por IP (429).\n\n" +
                PanelCookieNote);
        auth.MapPost("/refresh", RefreshAsync)
            .WithSessionResponses()
            .WithRequestExamples(ExampleBodies.Refresh)
            .WithOperationId("refreshSession")
            .WithSummary("Renovar la sesión")
            .WithDescription(
                "Cambia un `refresh_token` vigente por un `access_token` y un `refresh_token` nuevos (con el `email` del usuario, " +
                "que el JWT no lleva); el usado queda revocado. " +
                "Con `company_id` cambia la empresa activa del token. Un token vencido, revocado o desconocido responde 401 " +
                "(`identity.invalid_refresh_token`); presentar uno ya revocado revoca todas las sesiones del usuario (respuesta a " +
                "un token robado). Excepción: si la respuesta de una renovación se perdió (el token se rotó hace menos que el " +
                "período de gracia, 30 segundos por defecto, y su reemplazo nunca se usó), se entrega un par nuevo y el reemplazo " +
                "sin usar queda revocado. Si la base no responde a tiempo al revocar, responde 503 (`identity.store_unavailable`) " +
                "y se puede reintentar. Comparte el límite de intentos del inicio de sesión.\n\n" +
                PanelCookieNote + " Con `X-Client: panel` y sin `refresh_token` en el cuerpo, la renovación usa la cookie " +
                "(el cuerpo puede faltar o traer solo `company_id`) y la reemplaza por la nueva; si no hay cookie, o el token no " +
                "sirve, responde 401 y borra la cookie.");
        auth.MapPost("/logout", LogoutAsync)
            .Produces(StatusCodes.Status204NoContent)
            .WithRequestExamples(ExampleBodies.Logout)
            .WithOperationId("logout")
            .WithSummary("Cerrar la sesión")
            .WithDescription(
                "Revoca el `refresh_token` de la sesión, del cuerpo o, para el panel, de la cookie: esa sesión ya no puede " +
                "renovarse. Las demás sesiones del usuario (otros navegadores) siguen vivas, y el `access_token` que ya se " +
                "entregó vale hasta su vencimiento (15 minutos como máximo). Responde 204 también si el token no existe o ya " +
                "estaba revocado, así se puede repetir sin error; con un token recién rotado cuya renovación se perdió, revoca su " +
                "reemplazo sin usar. Comparte el límite de intentos del inicio de sesión.\n\n" +
                PanelCookieNote + " Con `X-Client: panel`, además, borra la cookie.");
        auth.MapPost("/password-reset/request", RequestPasswordResetAsync)
            .Produces<PasswordResetRequested>(StatusCodes.Status202Accepted)
            .WithRequestExamples(ExampleBodies.PasswordResetRequest)
            .WithOperationId("requestPasswordReset")
            .WithSummary("Pedir el enlace para restablecer la contraseña")
            .WithDescription(
                "Recibe el `email` de la cuenta y responde siempre 202 con el mismo `message`, exista o no la cuenta, para no " +
                "revelar qué correos están registrados. Si el correo es de un usuario activo, crea un enlace de un solo uso que " +
                "vence en 30 minutos y lo envía por correo (la página `/restablecer?token=...` del panel); pedir otro deja sin " +
                "efecto el anterior. Comparte el límite de intentos del inicio de sesión (429).");
        auth.MapPost("/password-reset/confirm", ConfirmPasswordResetAsync)
            .Produces(StatusCodes.Status204NoContent)
            .WithRequestExamples(ExampleBodies.PasswordResetConfirm)
            .WithOperationId("confirmPasswordReset")
            .WithSummary("Fijar la contraseña nueva")
            .WithDescription(
                "Recibe el `token` del enlace del correo y la contraseña nueva (`new_password`, de 10 a 256 caracteres). Si el " +
                "token sirve, cambia la contraseña, gasta el token y cierra todas las sesiones del usuario (revoca sus " +
                "`refresh_token`): debe volver a iniciar sesión. Responde 204. Un token desconocido, vencido, ya usado o " +
                "reemplazado por otro más nuevo responde el mismo 422 (`identity.invalid_reset_token`), sin decir cuál de los " +
                "casos es; una contraseña que no cumple la política responde 422 con el detalle en `errors.new_password` y no " +
                "gasta el token. Si dos confirmaciones con el mismo token llegan a la vez, o el usuario pide otro enlace " +
                "mientras tanto, solo una gana: la otra responde 409 (`persistence.conflict`) sin cambiar nada, y volver a " +
                "intentarla con ese token responde el mismo 422 (`identity.invalid_reset_token`). Comparte el límite de intentos del inicio " +
                "de sesión (429).");

        var apiKeys = endpoints.MapGroup("/api/v1/api-keys").WithTags(ApiTags.Identity).RequireAuthorization(IdentityPolicies.ManageCredentials);
        apiKeys.MapPost("/", CreateApiKeyAsync).AddEndpointFilter(NoStore)
            .Produces<CreatedApiKey>(StatusCodes.Status201Created)
            .WithRequestExamples(ExampleBodies.ApiKey)
            .WithOperationId("createApiKey")
            .WithSummary("Crear una API key")
            .WithDescription(
                "Crea una llave de acceso para una integración, con `name`, `scopes` y, si quiere, `expires_at`. Con una " +
                "credencial de empresa la llave es de esa empresa y solo puede llevar alcances que la credencial ya tiene " +
                "(nunca `platform:*`); con una credencial de plataforma es una llave de plataforma y solo lleva `platform:*`. " +
                "El texto completo de la llave (`key`) se muestra solo en esta respuesta. Alcances no permitidos: 403 " +
                "(`api_key.scope_not_allowed`).");
        apiKeys.MapGet("/", ListApiKeysAsync)
            .Produces<ApiKeyListResponse>()
            .WithOperationId("listApiKeys")
            .WithSummary("Listar las API keys")
            .WithDescription(
                "Devuelve en `items` las llaves de la empresa de la credencial (con una credencial de plataforma, las llaves de " +
                "plataforma), revocadas o no, de la más nueva a la más antigua: `id`, `name`, `key_prefix` (el inicio visible " +
                "de la llave, para reconocerla), `scopes`, `created_at`, `expires_at`, `last_used_at` (se guarda en segundo " +
                "plano y puede atrasarse unos segundos), `is_revoked` y `revoked_at`. Nunca trae el texto de la llave: solo se " +
                "muestra al crearla. Pagina por cursor: `limit` (20 por defecto, máximo 100) y `cursor` con el `next_cursor` de " +
                "la página anterior (nulo en la última). En una empresa, un usuario del panel debe ser su dueño (`owner`).");
        apiKeys.MapDelete("/{id:guid}", RevokeApiKeyAsync)
            .Produces(StatusCodes.Status204NoContent)
            .WithOperationId("revokeApiKey")
            .WithSummary("Revocar una API key")
            .WithDescription(
                "Revoca la llave con ese `id`; revocarla otra vez no cambia nada. Una empresa solo revoca sus propias llaves " +
                "(una de otra empresa responde 404, `api_key.not_found`); la plataforma puede revocar cualquiera. Las demás " +
                "instancias de la API dejan de aceptarla en máximo 60 segundos.");

        // Cualquier credencial autenticada (la política por defecto): cada una ve solo lo suyo.
        endpoints.MapGroup("/api/v1/me").WithTags(ApiTags.Identity).RequireAuthorization()
            .MapGet("/companies", ListMyCompaniesAsync)
            .Produces<MyCompanies>()
            .WithOperationId("listMyCompanies")
            .WithSummary("Listar mis empresas")
            .WithDescription(
                "Devuelve en `items` las empresas a las que puede entrar quien llama, ordenadas por razón social: `id`, `name` " +
                "(razón social), `nit`, `dv`, `roles` (los roles de quien llama en esa empresa, por ejemplo `owner` o `seller`) e " +
                "`is_active`. Es lo que muestra el selector de empresa del panel antes de renovar la sesión con `company_id`; no " +
                "necesita empresa activa y la empresa activa no reduce la lista. Un usuario del panel ve las empresas donde tiene " +
                "un rol con la pertenencia activa (un `platform_admin` sin empresas recibe la lista vacía); una API key de " +
                "empresa ve solo su empresa, sin roles; una de plataforma, la lista vacía. No se pagina: un usuario pertenece a pocas empresas.");

        endpoints.MapGroup("/api/v1/users").WithTags(ApiTags.Identity).RequireAuthorization(IdentityPolicies.ManageCredentials)
            .MapPost("/", CreateUserAsync)
            .Produces<CreatedUser>(StatusCodes.Status201Created)
            .WithRequestExamples(ExampleBodies.User)
            .WithOperationId("createUser")
            .WithSummary("Crear un usuario del panel")
            .WithDescription(
                "Crea un usuario con `email`, `password` (al menos 10 caracteres), `name`, `role` y, si quiere, `phone`. El dueño (`owner`) de una " +
                "empresa, con su sesión del panel, crea usuarios de su empresa con rol `owner` o `seller`; la plataforma " +
                "crea administradores de plataforma (`platform_admin`) y, con `company_id`, el primer dueño (`owner`) o un vendedor " +
                "(`seller`) de una empresa. Las API keys de empresa no crean usuarios. Correo repetido: 409 " +
                "(`user.email_taken`); rol no permitido: 403 (`user.role_not_allowed`); empresa inexistente: 404 " +
                "(`user.company_not_found`); `company_id` de otra empresa con una credencial de empresa: 403.");

        // Users of one company, for its owner or the platform. They are company routes (/api/v1/companies/{nit}/...),
        // so they carry the Companies tag and answer another company's NIT like the configuration routes do.
        var companyUsers = endpoints.MapGroup("/api/v1/companies/{nit}/users").WithTags(ApiTags.Companies)
            .RequireAuthorization(IdentityPolicies.ManageCredentials);
        companyUsers.MapGet("/", ListCompanyUsersAsync)
            .Produces<CompanyUserList>()
            .WithOperationId("listCompanyUsers")
            .WithSummary("Listar los usuarios de la empresa")
            .WithDescription(
                "Devuelve en `items` los usuarios del panel de la empresa, dueños y vendedores, con su pertenencia activa o " +
                "no, ordenados por nombre y luego por correo: `id`, `email`, `name`, `phone`, `role` (`owner` o `seller`) e " +
                "`is_active` (si la pertenencia a esta empresa está activa). Nunca trae contraseñas ni tokens. Los clientes " +
                "del portal no aparecen. No se pagina: una empresa tiene pocos usuarios. Lo usa el dueño (`owner`) de la " +
                "empresa con su sesión del panel; la plataforma puede consultar cualquier empresa (NIT inexistente: 404, " +
                "`user.company_not_found`)." + CompanyUsersNit);
        companyUsers.MapPatch("/{userId:guid}", UpdateCompanyUserAsync)
            .Produces<CompanyUser>()
            .WithRequestExamples(CompanyUserExamples)
            .WithOperationId("updateCompanyUser")
            .WithSummary("Cambiar el rol o el estado de un usuario de la empresa")
            .WithDescription(
                "Cambia el rol (`role`: `owner` o `seller`) del usuario `{userId}` en la empresa, o activa o desactiva su " +
                "pertenencia a ella (`is_active`); el campo que no llega queda como estaba. Responde el usuario como lo " +
                "lista `GET /api/v1/companies/{nit}/users`. Desactivar no borra la cuenta ni lo que el usuario emitió: solo " +
                "le cierra esta empresa. Ya no puede entrar a ella al iniciar sesión, elegirla ni renovar la sesión hacia " +
                "ella, y sus sesiones se cierran (se revocan todos sus `refresh_token`; el `access_token` ya entregado vale " +
                "hasta vencer, 15 minutos como máximo). Sus demás empresas no cambian. Reactivar le devuelve el acceso. " +
                "Errores: usuario que no existe o no es de la empresa, 404 (`user.not_found`); desactivarse a sí mismo, 422 " +
                "(`user.cannot_deactivate_self`); quitarse a sí mismo el rol de dueño, 422 " +
                "(`user.cannot_remove_own_owner_role`); dejar la empresa sin ningún dueño activo, 422 (`user.last_owner`); " +
                "rol desconocido o cuerpo sin `role` ni `is_active`, 422 con el detalle en `errors.role`; otro cambio " +
                "simultáneo sobre los dueños, 409 (`persistence.conflict`)." + CompanyUsersNit)
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                foreach (var parameter in operation.Parameters?.OfType<OpenApiParameter>() ?? [])
                {
                    if (parameter.Name == "userId")
                    {
                        parameter.Example ??= ExampleUserId;
                    }
                }

                return Task.CompletedTask;
            });
    }

    /// <summary>Reminder of the company user routes: another company's NIT answers like one that does not exist.</summary>
    private const string CompanyUsersNit =
        " Con una credencial de empresa, el `{nit}` debe ser el de esa empresa y quien llama debe ser uno de sus dueños " +
        "activos con su sesión del panel; si no, responde 403 (`company.forbidden`), exista o no el NIT. Las API keys de " +
        "empresa no administran usuarios: responden ese mismo 403, tengan los alcances que tengan. Sin `config:write` " +
        "(por ejemplo, un vendedor) también responde 403, para cualquier NIT.";

    private const string ExampleUserId = "0196f3a2-7c41-7d2e-9b8a-3c4d5e6f7a8c";

    private static readonly RequestExample[] CompanyUserExamples =
    [
        new("desactivar", "Desactivar la pertenencia del usuario a la empresa", JsonNode.Parse("""{ "is_active": false }""")!),
        new("reactivar", "Reactivar la pertenencia del usuario a la empresa", JsonNode.Parse("""{ "is_active": true }""")!),
        new("hacer-dueno", "Dar el rol de dueño", JsonNode.Parse("""{ "role": "owner" }""")!),
        new("hacer-vendedor", "Dar el rol de vendedor", JsonNode.Parse("""{ "role": "seller" }""")!),
    ];

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
        // Un integrador (sin X-Client: panel) siempre manda el token en el cuerpo: sin cuerpo, el mismo error de un
        // cuerpo ilegible que respondía antes de que el panel pudiera omitirlo.
        if (body is null && !isPanel)
        {
            return InvalidRequestBodyHandler.InvalidBody.ToHttpResult();
        }

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

    /// <summary>
    /// Sin token no hay nada que revocar: responde 204 sin llamar al caso de uso. La revocación no usa la cancelación
    /// de la petición: el panel limpia la sesión y sigue sin esperar la respuesta, y si el navegador corta la petición
    /// (cierra o recarga la pestaña) el token de renovación quedaría vivo. Se acota con su propio plazo
    /// (<see cref="ISessionPolicy.RevocationTimeout"/>, 10 segundos); si se cumple, responde el 503
    /// <c>identity.store_unavailable</c> de una base caída, sin borrar la cookie, para que el panel lo intente otra vez.
    /// </summary>
    private static async Task<IResult> LogoutAsync(
        LogoutBody? body,
        [FromHeader(Name = PanelSession.ClientHeader)] string? client,
        HttpContext context,
        [FromServices] IHandler<LogoutCommand, LoggedOut> handler,
        [FromServices] ISessionPolicy policy)
    {
        var isPanel = PanelSession.IsPanel(client);
        var presented = PanelSession.ReadRefreshToken(context.Request, body?.RefreshToken, isPanel);
        if (!presented.IsSuccess)
        {
            return presented.Error.ToHttpResult();
        }

        if (presented.Value.Value.Length > 0)
        {
            var result = await SessionRevocation.WithinDeadlineAsync(
                policy.RevocationTimeout, deadline => handler.HandleAsync(new LogoutCommand(presented.Value.Value), deadline));
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

    private static async Task<IResult> ListApiKeysAsync(
        [FromQuery(Name = "cursor")] string? cursor,
        [FromQuery(Name = "limit")] int? limit,
        [FromServices] IHandler<ListApiKeysQuery, ApiKeyListResponse> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new ListApiKeysQuery(cursor, limit), ct)).ToHttpResult();

    private static async Task<IResult> RevokeApiKeyAsync(
        Guid id, [FromServices] IHandler<RevokeApiKeyCommand, RevokedApiKey> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new RevokeApiKeyCommand(id), ct)).ToHttpResult(_ => TypedResults.NoContent());

    private static async Task<IResult> CreateUserAsync(
        CreateUserBody body, [FromServices] IHandler<CreateUserCommand, CreatedUser> handler, CancellationToken ct) =>
        (await handler.HandleAsync(
            new CreateUserCommand(body.Email ?? "", body.Password ?? "", body.Name ?? "", body.Phone, body.Role ?? "", body.CompanyId), ct))
        .ToHttpResult(created => TypedResults.Created($"/api/v1/users/{created.Id}", created));

    private static async Task<IResult> ListCompanyUsersAsync(
        string nit, [FromServices] IHandler<ListCompanyUsersQuery, CompanyUserList> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new ListCompanyUsersQuery(nit), ct)).ToHttpResult();

    private static async Task<IResult> UpdateCompanyUserAsync(
        string nit,
        Guid userId,
        UpdateCompanyUserBody body,
        [FromServices] IHandler<UpdateCompanyUserCommand, CompanyUser> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new UpdateCompanyUserCommand(nit, userId, body.Role, body.IsActive), ct)).ToHttpResult();

    // Un choque al guardar (otra solicitud simultánea del mismo usuario) solo puede pasar con un correo registrado: si
    // saliera como 409 revelaría que la cuenta existe. Por eso responde el mismo 202 que en los demás casos. El registro
    // no lleva el correo ni el token.
    private static async Task<IResult> RequestPasswordResetAsync(
        PasswordResetRequestBody body,
        HttpContext context,
        [FromServices] IHandler<RequestPasswordResetCommand, PasswordResetRequested> handler,
        [FromServices] ILogger<IdentityEndpoints> logger,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new RequestPasswordResetCommand(body.Email ?? "", ClientIp(context)), ct);
        if (!result.IsSuccess && result.Error.Type == ErrorType.Conflict)
        {
            logger.LogWarning(
                "A password reset request conflicted with a simultaneous request ({ErrorCode}); it answers the usual 202.",
                result.Error.Code);
            result = PasswordResetRequested.Instance;
        }

        return result.ToHttpResult(requested => TypedResults.Accepted((string?)null, requested));
    }

    private static async Task<IResult> ConfirmPasswordResetAsync(
        PasswordResetConfirmBody body,
        [FromServices] IHandler<ConfirmPasswordResetCommand, PasswordResetConfirmed> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new ConfirmPasswordResetCommand(body.Token ?? "", body.NewPassword ?? ""), ct))
        .ToHttpResult(_ => TypedResults.NoContent());

    private static async Task<IResult> ListMyCompaniesAsync(
        [FromServices] IHandler<ListMyCompaniesQuery, MyCompanies> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new ListMyCompaniesQuery(), ct)).ToHttpResult();

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

    internal sealed record PasswordResetRequestBody([property: JsonPropertyName("email")] string? Email)
    {
        public override string ToString() => "PasswordResetRequestBody";
    }

    internal sealed record PasswordResetConfirmBody(
        [property: JsonPropertyName("token")] string? Token,
        [property: JsonPropertyName("new_password")] string? NewPassword)
    {
        public override string ToString() => "PasswordResetConfirmBody";
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
        [property: JsonPropertyName("role")] string? Role,
        [property: JsonPropertyName("company_id")] Guid? CompanyId = null)
    {
        public override string ToString() => "CreateUserBody";
    }

    /// <summary>Both fields are optional; the validator asks for at least one.</summary>
    internal sealed record UpdateCompanyUserBody(
        [property: JsonPropertyName("role")] string? Role = null,
        [property: JsonPropertyName("is_active")] bool? IsActive = null);
}
