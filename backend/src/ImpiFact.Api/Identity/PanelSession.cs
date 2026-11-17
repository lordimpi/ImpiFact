using System.Net;
using System.Text.Json.Serialization;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Identity.Sessions;

namespace ImpiFact.Api.Identity;

/// <summary>
/// Sesión del panel en el navegador (Arquitectura frontend, sección 6 y ADR-20): el token de acceso (JWT, JSON Web
/// Token) vive solo en la memoria del panel y el token de renovación (refresh token) viaja en una cookie que el
/// JavaScript de la página no puede leer. Así un ataque XSS (Cross-Site Scripting: código ajeno que corre dentro de la
/// página) no puede robar la sesión larga.
/// </summary>
/// <remarks>
/// <para>
/// El panel se identifica con el encabezado <c>X-Client: panel</c> en <c>login</c>, <c>refresh</c> y <c>logout</c>.
/// Sin él, las rutas responden igual que siempre a las integraciones (el token de renovación en el cuerpo).
/// </para>
/// <para>
/// Defensa contra CSRF (Cross-Site Request Forgery: otra página hace que el navegador envíe una solicitud con la cookie
/// del usuario), en dos capas independientes:
/// </para>
/// <list type="number">
/// <item><c>SameSite=Strict</c>: el navegador no envía la cookie en solicitudes que salen de otro sitio.</item>
/// <item>
/// La cookie solo se acepta si la solicitud trae <c>X-Client: panel</c>. Un formulario HTML no puede poner encabezados,
/// y una página de otro origen solo puede ponerlos después de una consulta previa de CORS (preflight), que la API
/// solo aprueba para los orígenes de <c>Cors:Origins</c>. Esto cubre también un subdominio vecino del mismo sitio,
/// donde <c>SameSite</c> no protege.
/// </item>
/// </list>
/// <para>
/// <c>Strict</c> y no <c>Lax</c>: la cookie solo la usan llamadas <c>fetch</c> del panel, nunca una navegación, así
/// que <c>Lax</c> no da nada y sí deja pasar la cookie al llegar desde un enlace de otro sitio. Requiere que el panel y
/// la API estén en el mismo sitio (el mismo dominio registrable, por ejemplo <c>app.example.com</c> y
/// <c>api.example.com</c>); en desarrollo, los dos con el mismo nombre de host (<c>localhost</c> o <c>127.0.0.1</c>, no
/// mezclados).
/// </para>
/// </remarks>
internal static class PanelSession
{
    /// <summary>Encabezado con el que el panel pide la sesión por cookie.</summary>
    public const string ClientHeader = "X-Client";

    /// <summary>Valor de <see cref="ClientHeader"/> que identifica al panel.</summary>
    public const string PanelClient = "panel";

    /// <summary>Nombre de la cookie que lleva el token de renovación.</summary>
    public const string CookieName = "impifact_refresh";

    /// <summary>La cookie solo viaja a las rutas de sesión, nunca al resto de la API.</summary>
    public const string CookiePath = "/api/v1/auth";

    /// <summary>Una cookie de sesión llegó sin <c>X-Client: panel</c>: pudo enviarla otra página (CSRF).</summary>
    public static readonly Error ClientHeaderRequired = new(
        "identity.client_header_required",
        "La cookie de sesión solo se acepta con el encabezado X-Client: panel.",
        ErrorType.Forbidden);

    public static bool IsPanel(string? clientHeader) =>
        string.Equals(clientHeader?.Trim(), PanelClient, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// El token de renovación que presenta la solicitud: el del cuerpo, si viene; si no, el de la cookie, que solo se
    /// acepta con <c>X-Client: panel</c> (si falta, <see cref="ClientHeaderRequired"/>). Sin ninguno de los dos, un
    /// token vacío.
    /// </summary>
    public static Result<PresentedRefreshToken> ReadRefreshToken(HttpRequest request, string? bodyToken, bool isPanel)
    {
        if (!string.IsNullOrEmpty(bodyToken))
        {
            return new PresentedRefreshToken(bodyToken, FromCookie: false);
        }

        if (!request.Cookies.TryGetValue(CookieName, out var cookie) || string.IsNullOrEmpty(cookie))
        {
            return new PresentedRefreshToken("", FromCookie: false);
        }

        return isPanel ? new PresentedRefreshToken(cookie, FromCookie: true) : ClientHeaderRequired;
    }

    /// <summary>Pone el token de renovación en la cookie y responde 200 con la sesión sin ese token.</summary>
    public static IResult Issue(HttpContext context, SessionTokens tokens)
    {
        context.Response.Cookies.Append(CookieName, tokens.RefreshToken, CookieOptionsFor(context, tokens.RefreshTokenExpiresAt));
        return TypedResults.Ok(PanelSessionTokens.From(tokens));
    }

    /// <summary>Le pide al navegador borrar la cookie (la misma ruta y los mismos atributos, ya vencida).</summary>
    public static void Clear(HttpContext context) =>
        context.Response.Cookies.Delete(CookieName, CookieOptionsFor(context, expires: null));

    private static CookieOptions CookieOptionsFor(HttpContext context, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = !InsecureAllowed(context),
        SameSite = SameSiteMode.Strict,
        Path = context.Request.PathBase.Add(CookiePath).Value,
        Expires = expires,
        IsEssential = true,
    };

    /// <summary>
    /// Solo en Development, por HTTP sin cifrar y hacia la propia máquina, la cookie sale sin <c>Secure</c>, para
    /// probar el panel en <c>http://localhost</c> o <c>http://127.0.0.1</c>. En cualquier otro caso lleva <c>Secure</c>,
    /// también detrás de un proxy que termina HTTPS.
    /// </summary>
    private static bool InsecureAllowed(HttpContext context) =>
        !context.Request.IsHttps
        && context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment()
        && IsLoopback(context.Request.Host.Host);

    private static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));
}

/// <param name="Value">El token, o vacío si la solicitud no trajo ninguno.</param>
/// <param name="FromCookie">Si salió de la cookie del panel y no del cuerpo.</param>
internal readonly record struct PresentedRefreshToken(string Value, bool FromCookie)
{
    /// <summary>No incluye el token, para que un registro accidental del objeto no lo filtre.</summary>
    public override string ToString() => $"PresentedRefreshToken {{ FromCookie = {FromCookie} }}";
}

/// <summary>
/// La sesión que recibe el panel: igual a <see cref="SessionTokens"/> pero sin <c>refresh_token</c>, que va en la
/// cookie. Conserva <c>refresh_token_expires_at</c> para que el panel sepa hasta cuándo dura la sesión.
/// </summary>
internal sealed record PanelSessionTokens(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_at")] DateTimeOffset AccessTokenExpiresAt,
    [property: JsonPropertyName("refresh_token_expires_at")] DateTimeOffset RefreshTokenExpiresAt,
    [property: JsonPropertyName("company_id")] Guid? CompanyId,
    [property: JsonPropertyName("roles")] IReadOnlyList<string> Roles,
    [property: JsonPropertyName("companies")] IReadOnlyList<Guid> Companies)
{
    [JsonPropertyName("token_type")]
    public string TokenType => "Bearer";

    public static PanelSessionTokens From(SessionTokens tokens) => new(
        tokens.AccessToken, tokens.AccessTokenExpiresAt, tokens.RefreshTokenExpiresAt, tokens.CompanyId, tokens.Roles, tokens.Companies);

    /// <summary>No incluye el token de acceso, para que un registro accidental del objeto no lo filtre.</summary>
    public override string ToString() => $"PanelSessionTokens {{ CompanyId = {CompanyId}, ExpiresAt = {AccessTokenExpiresAt:O} }}";
}
