using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Identity;
using ImpiFact.Infrastructure.Identity;
using ImpiFact.Integration.Tests.Identity;
using HttpMethods = Microsoft.AspNetCore.Http.HttpMethods;
using HostString = Microsoft.AspNetCore.Http.HostString;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Net.Http.Headers;

// Vive en el espacio de nombres de IdentityData para usar su PostgreSQL de prueba: el SetUpFixture que lo levanta
// (IdentityDatabase) solo cubre ese espacio de nombres.
namespace ImpiFact.Integration.Tests.IdentityData;

/// <summary>
/// Sesión del panel (Arquitectura frontend, sección 6 y ADR-20): con el encabezado <c>X-Client: panel</c>, el token de
/// renovación (refresh token) viaja en una cookie <c>HttpOnly</c> y no en el cuerpo; la renovación lo lee de la
/// cookie y la rota; el cierre de sesión lo revoca y borra la cookie. Una cookie sin ese encabezado no sirve, así
/// otra página no puede usarla (CSRF, Cross-Site Request Forgery: solicitud falsificada desde otro sitio). Corre contra
/// PostgreSQL.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class PanelSessionTests
{
    private const string Password = "Contraseña-Segura-1";
    private const string CookieName = "impifact_refresh";
    private const string ClientHeader = "X-Client";

    private IdentityDataApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartApi() => _factory = new IdentityDataApiFactory();

    [OneTimeTearDown]
    public void StopApi() => _factory.Dispose();

    [Test]
    public async Task Panel_login_sets_the_refresh_cookie_and_leaves_the_token_out_of_the_body()
    {
        var (userId, email) = await AddUserAsync();

        using var response = await PanelLogin(email);
        var body = await ReadAsync(response);
        var cookie = RefreshCookie(response);
        var stored = await QuerySingleAsync<long>(
            "select count(*) from refresh_tokens where user_id = @userId and token_hash = @hash and revoked_at is null",
            new { userId, hash = CredentialHash.Sha256Hex(cookie.Value.Value!) });

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body.TryGetProperty("refresh_token", out _), Is.False, "The panel never sees the refresh token.");
            Assert.That(body.GetProperty("access_token").GetString(), Is.Not.Empty);
            Assert.That(body.GetProperty("token_type").GetString(), Is.EqualTo("Bearer"));
            Assert.That(cookie.HttpOnly, Is.True);
            Assert.That(cookie.Secure, Is.True);
            Assert.That(cookie.SameSite, Is.EqualTo(SameSiteMode.Strict));
            Assert.That(cookie.Path.Value, Is.EqualTo("/api/v1/auth"));
            Assert.That(cookie.Domain.HasValue, Is.False, "The cookie stays on the API host.");
            Assert.That(cookie.Expires, Is.EqualTo(body.GetProperty("refresh_token_expires_at").GetDateTimeOffset()).Within(TimeSpan.FromSeconds(1)));
            Assert.That(stored, Is.EqualTo(1));
            Assert.That(_factory.Logs.Entries, Has.None.Contains(cookie.Value.Value!));
        });
    }

    [Test]
    public async Task In_development_over_plain_http_on_loopback_the_cookie_is_not_secure()
    {
        var (_, email) = await AddUserAsync();
        using var client = Client("http://localhost");

        using var response = await client.SendAsync(PanelRequest("/api/v1/auth/login", new { email, password = Password }));

        Assert.That(RefreshCookie(response).Secure, Is.False);
    }

    [Test]
    public async Task Api_client_refresh_without_a_body_keeps_the_invalid_body_error()
    {
        using var response = await Client().PostAsync("/api/v1/auth/refresh", content: null);

        Assert.Multiple(() =>
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(422));
            Assert.That(Code(response), Is.EqualTo("request.invalid_body"));
        });
    }

    [Test]
    public async Task A_request_with_the_refresh_cookie_twice_is_rejected_and_the_session_survives()
    {
        var (_, email) = await AddUserAsync();
        var valid = RefreshCookie(await PanelLogin(email)).Value.Value!;
        using var request = PanelRequest("/api/v1/auth/refresh", body: null);
        request.Headers.Add(HeaderNames.Cookie, $"{CookieName}=planted-by-a-sibling-subdomain; {CookieName}={valid}");

        using var response = await Client().SendAsync(request);
        var revokedAt = await QuerySingleAsync<DateTime?>(
            "select revoked_at from refresh_tokens where token_hash = @hash", new { hash = CredentialHash.Sha256Hex(valid) });

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(Code(response), Is.EqualTo("identity.invalid_refresh_token"));
            Assert.That(revokedAt, Is.Null, "The real session is not rotated nor revoked.");
        });
    }

    [Test]
    public async Task Api_client_login_keeps_the_token_in_the_body_and_sets_no_cookie()
    {
        var (_, email) = await AddUserAsync();

        using var response = await Client().PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        var body = await ReadAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body.GetProperty("refresh_token").GetString(), Is.Not.Empty);
            Assert.That(response.Headers.Contains(HeaderNames.SetCookie), Is.False);
        });
    }

    [Test]
    public async Task Refresh_from_the_cookie_rotates_the_token_and_sets_the_new_cookie()
    {
        var (_, email) = await AddUserAsync();
        var original = RefreshCookie(await PanelLogin(email)).Value.Value!;

        using var response = await Client().SendAsync(PanelRequest("/api/v1/auth/refresh", body: null, cookie: original));
        var body = await ReadAsync(response);
        var rotated = RefreshCookie(response);
        var oldRow = await QuerySingleAsync<(DateTime? RevokedAt, Guid? ReplacedBy)>(
            "select revoked_at, replaced_by from refresh_tokens where token_hash = @hash", new { hash = CredentialHash.Sha256Hex(original) });
        var newId = await QuerySingleAsync<Guid>(
            "select id from refresh_tokens where token_hash = @hash", new { hash = CredentialHash.Sha256Hex(rotated.Value.Value!) });

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(body.TryGetProperty("refresh_token", out _), Is.False);
            Assert.That(body.GetProperty("access_token").GetString(), Is.Not.Empty);
            Assert.That(rotated.Value.Value, Is.Not.EqualTo(original));
            Assert.That(rotated.HttpOnly, Is.True);
            Assert.That(rotated.SameSite, Is.EqualTo(SameSiteMode.Strict));
            Assert.That(rotated.Path.Value, Is.EqualTo("/api/v1/auth"));
            Assert.That(oldRow.RevokedAt, Is.Not.Null);
            Assert.That(oldRow.ReplacedBy, Is.EqualTo(newId));
        });
    }

    [Test]
    public async Task Refresh_from_the_cookie_honors_the_company_in_the_body()
    {
        var companyId = await IdentityDatabase.AddCompanyAsync();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        await IdentityStoreTests.AddUserAsync([], new CompanyMembership(companyId, RoleNames.Owner), email, Password);
        var cookie = RefreshCookie(await PanelLogin(email)).Value.Value!;

        using var foreign = await Client().SendAsync(PanelRequest("/api/v1/auth/refresh", new { company_id = Guid.CreateVersion7() }, cookie));
        using var own = await Client().SendAsync(PanelRequest("/api/v1/auth/refresh", new { company_id = companyId }, cookie));
        var body = await ReadAsync(own);

        Assert.Multiple(() =>
        {
            Assert.That(foreign.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(Code(foreign), Is.EqualTo("identity.company_not_allowed"));
            Assert.That(own.StatusCode, Is.EqualTo(HttpStatusCode.OK), "A refused company switch leaves the token usable.");
            Assert.That(body.GetProperty("company_id").GetGuid(), Is.EqualTo(companyId));
        });
    }

    [Test]
    public async Task Panel_refresh_without_a_cookie_answers_401_like_a_missing_session()
    {
        using var response = await Client().SendAsync(PanelRequest("/api/v1/auth/refresh", body: null));

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(Code(response), Is.EqualTo("identity.invalid_refresh_token"));
        });
    }

    [Test]
    public async Task A_reused_refresh_cookie_revokes_every_session_of_the_user_and_clears_the_cookie()
    {
        var (userId, email) = await AddUserAsync();
        var original = RefreshCookie(await PanelLogin(email)).Value.Value!;
        using var rotated = await Client().SendAsync(PanelRequest("/api/v1/auth/refresh", body: null, cookie: original));
        // El reemplazo se usa: así el token viejo que vuelve ya no puede ser una respuesta perdida (período de gracia).
        using var rotatedAgain = await Client().SendAsync(
            PanelRequest("/api/v1/auth/refresh", body: null, cookie: RefreshCookie(rotated).Value.Value!));

        using var reused = await Client().SendAsync(PanelRequest("/api/v1/auth/refresh", body: null, cookie: original));
        var cleared = RefreshCookie(reused);
        var active = await QuerySingleAsync<long>(
            "select count(*) from refresh_tokens where user_id = @userId and revoked_at is null", new { userId });

        Assert.Multiple(() =>
        {
            Assert.That(rotated.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(rotatedAgain.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(reused.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(Code(reused), Is.EqualTo("identity.invalid_refresh_token"));
            Assert.That(active, Is.Zero, "The reuse also revoked the token that replaced it.");
            Assert.That(cleared.Value.Value, Is.Empty);
            Assert.That(cleared.Expires, Is.LessThan(DateTimeOffset.UtcNow));
        });
    }

    // Respuesta perdida: el navegador cortó la renovación justo después de que el servidor rotara el token y se quedó
    // con la cookie vieja. Presentarla dentro del período de gracia (30 segundos), con el reemplazo sin usar, revoca ese
    // reemplazo y entrega un par nuevo de la misma sesión; las demás sesiones del usuario siguen vivas.
    [Test]
    public async Task A_cookie_kept_after_a_lost_refresh_response_gets_a_fresh_pair_within_the_grace_period()
    {
        var companyId = await IdentityDatabase.AddCompanyAsync();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        var userId = await IdentityStoreTests.AddUserAsync([], new CompanyMembership(companyId, RoleNames.Owner), email, Password);
        var original = RefreshCookie(await PanelLogin(email)).Value.Value!;
        var otherSession = (await ReadAsync(await Client().PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password })))
            .GetProperty("refresh_token").GetString()!;
        // El servidor rota el token, pero esta respuesta nunca llega al navegador.
        using var lostResponse = await Client().SendAsync(PanelRequest("/api/v1/auth/refresh", body: null, cookie: original));
        var lost = RefreshCookie(lostResponse).Value.Value!;

        using var retried = await Client().SendAsync(PanelRequest("/api/v1/auth/refresh", body: null, cookie: original));
        var body = await ReadAsync(retried);
        var fresh = RefreshCookie(retried);
        var presentedRow = await TokenRowAsync(original);
        var lostRow = await TokenRowAsync(lost);
        var freshRow = await TokenRowAsync(fresh.Value.Value!);
        var otherRow = await TokenRowAsync(otherSession);

        Assert.Multiple(() =>
        {
            Assert.That(lostResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(retried.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body.GetProperty("access_token").GetString(), Is.Not.Empty);
            Assert.That(body.TryGetProperty("refresh_token", out _), Is.False);
            Assert.That(body.GetProperty("company_id").GetGuid(), Is.EqualTo(companyId), "Same user, same company.");
            Assert.That(fresh.Value.Value, Is.Not.EqualTo(original).And.Not.EqualTo(lost));
            Assert.That(fresh.HttpOnly, Is.True);
            Assert.That(fresh.Path.Value, Is.EqualTo("/api/v1/auth"));
            Assert.That(presentedRow.ReplacedBy, Is.EqualTo(lostRow.Id), "The presented token keeps its first rotation.");
            Assert.That(lostRow.RevokedAt, Is.Not.Null, "The replacement the browser never received is revoked.");
            Assert.That(lostRow.ReplacedBy, Is.EqualTo(freshRow.Id));
            Assert.That(freshRow.RevokedAt, Is.Null);
            Assert.That(freshRow.UserId, Is.EqualTo(userId));
            Assert.That(otherRow.RevokedAt, Is.Null, "A lost response is not a theft: the other sessions stay alive.");
        });

        // El par nuevo sirve como cualquier otro.
        using var next = await Client().SendAsync(PanelRequest("/api/v1/auth/refresh", body: null, cookie: fresh.Value.Value!));
        Assert.That(next.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Logout_revokes_the_cookie_session_clears_the_cookie_and_can_be_repeated()
    {
        var (_, email) = await AddUserAsync();
        var cookie = RefreshCookie(await PanelLogin(email)).Value.Value!;
        var otherSession = (await ReadAsync(await Client().PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password })))
            .GetProperty("refresh_token").GetString()!;

        using var first = await Client().SendAsync(PanelRequest("/api/v1/auth/logout", body: null, cookie: cookie));
        var cleared = RefreshCookie(first);
        var revokedAt = await QuerySingleAsync<DateTime?>(
            "select revoked_at from refresh_tokens where token_hash = @hash", new { hash = CredentialHash.Sha256Hex(cookie) });
        using var second = await Client().SendAsync(PanelRequest("/api/v1/auth/logout", body: null, cookie: cookie));
        var otherRevokedAt = await QuerySingleAsync<DateTime?>(
            "select revoked_at from refresh_tokens where token_hash = @hash", new { hash = CredentialHash.Sha256Hex(otherSession) });

        Assert.Multiple(() =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(first.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(revokedAt, Is.Not.Null);
            Assert.That(cleared.Value.Value, Is.Empty);
            Assert.That(cleared.Expires, Is.LessThan(DateTimeOffset.UtcNow));
            Assert.That(cleared.Path.Value, Is.EqualTo("/api/v1/auth"));
            Assert.That(cleared.HttpOnly, Is.True);
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "Logging out again is not an error.");
            Assert.That(otherRevokedAt, Is.Null, "Logging out a second time does not end the other sessions of the user.");
        });

        using var refreshAfterLogout = await Client().SendAsync(PanelRequest("/api/v1/auth/refresh", body: null, cookie: cookie));
        Assert.That(refreshAfterLogout.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Logout_revokes_the_session_even_when_the_browser_aborts_the_request()
    {
        var (_, email) = await AddUserAsync();
        var cookie = RefreshCookie(await PanelLogin(email)).Value.Value!;

        // El navegador ya cortó la petición (se cerró o recargó la pestaña): RequestAborted llega cancelado.
        var context = await _factory.Server.SendAsync(http =>
        {
            http.Request.Method = HttpMethods.Post;
            http.Request.Scheme = "https";
            http.Request.Host = new HostString("localhost");
            http.Request.Path = "/api/v1/auth/logout";
            http.Request.Headers[ClientHeader] = "panel";
            http.Request.Headers[HeaderNames.Cookie] = $"{CookieName}={cookie}";
            http.RequestAborted = new CancellationToken(canceled: true);
        });
        var revokedAt = await QuerySingleAsync<DateTime?>(
            "select revoked_at from refresh_tokens where token_hash = @hash", new { hash = CredentialHash.Sha256Hex(cookie) });

        Assert.Multiple(() =>
        {
            Assert.That(revokedAt, Is.Not.Null, "Closing the session must finish even if nobody waits for the answer.");
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status204NoContent));
        });
    }

    [Test]
    public async Task Logout_with_the_token_in_the_body_revokes_it_for_api_clients()
    {
        var (_, email) = await AddUserAsync();
        var token = (await ReadAsync(await Client().PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password })))
            .GetProperty("refresh_token").GetString()!;

        using var response = await Client().PostAsJsonAsync("/api/v1/auth/logout", new { refresh_token = token });
        var revokedAt = await QuerySingleAsync<DateTime?>(
            "select revoked_at from refresh_tokens where token_hash = @hash", new { hash = CredentialHash.Sha256Hex(token) });
        using var refresh = await Client().PostAsJsonAsync("/api/v1/auth/refresh", new { refresh_token = token });

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(response.Headers.Contains(HeaderNames.SetCookie), Is.False, "An API client gets no cookie.");
            Assert.That(revokedAt, Is.Not.Null);
            Assert.That(refresh.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        });
    }

    [TestCase(null)]
    [TestCase("""{"refresh_token": "token-que-no-existe"}""")]
    public async Task Logout_without_a_known_token_answers_204(string? body)
    {
        using var request = PanelRequest("/api/v1/auth/logout", body: null);
        if (body is not null)
        {
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        }

        using var response = await Client().SendAsync(request);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(RefreshCookie(response).Value.Value, Is.Empty, "The panel cookie is cleared anyway.");
        });
    }

    [TestCase("/api/v1/auth/refresh")]
    [TestCase("/api/v1/auth/logout")]
    public async Task A_cookie_without_the_panel_header_is_rejected_and_the_session_stays_untouched(string path)
    {
        var (_, email) = await AddUserAsync();
        var cookie = RefreshCookie(await PanelLogin(email)).Value.Value!;
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(new { }) };
        request.Headers.Add(HeaderNames.Cookie, $"{CookieName}={cookie}");

        using var response = await Client().SendAsync(request);
        var revokedAt = await QuerySingleAsync<DateTime?>(
            "select revoked_at from refresh_tokens where token_hash = @hash", new { hash = CredentialHash.Sha256Hex(cookie) });

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(Code(response), Is.EqualTo("identity.client_header_required"));
            Assert.That(revokedAt, Is.Null, "A request another site could forge does not touch the session.");
        });
    }

    private async Task<(Guid Id, string Email)> AddUserAsync()
    {
        var email = $"user-{Guid.NewGuid():N}@example.com";
        var id = await IdentityStoreTests.AddUserAsync([], company: null, email, Password);
        return (id, email);
    }

    private async Task<HttpResponseMessage> PanelLogin(string email, Guid? companyId = null) =>
        await Client().SendAsync(PanelRequest("/api/v1/auth/login", new { email, password = Password, company_id = companyId }));

    /// <summary>Cliente sin manejo automático de cookies: cada prueba decide qué cookie manda. Por defecto, HTTPS.</summary>
    private HttpClient Client(string baseAddress = "https://localhost") =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(baseAddress), HandleCookies = false });

    private static HttpRequestMessage PanelRequest(string path, object? body, string? cookie = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.Add(ClientHeader, "panel");
        if (cookie is not null)
        {
            request.Headers.Add(HeaderNames.Cookie, $"{CookieName}={cookie}");
        }

        return request;
    }

    private static SetCookieHeaderValue RefreshCookie(HttpResponseMessage response)
    {
        Assert.That(response.Headers.TryGetValues(HeaderNames.SetCookie, out var values), Is.True, "The response sets no cookie.");
        return SetCookieHeaderValue.ParseList(values!.ToList()).Single(cookie => cookie.Name.Value == CookieName);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static string? Code(HttpResponseMessage response) =>
        JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement.GetProperty("code").GetString();

    private static Task<(Guid Id, Guid UserId, DateTime? RevokedAt, Guid? ReplacedBy)> TokenRowAsync(string token) =>
        QuerySingleAsync<(Guid, Guid, DateTime?, Guid?)>(
            "select id, user_id, revoked_at, replaced_by from refresh_tokens where token_hash = @hash",
            new { hash = CredentialHash.Sha256Hex(token) });

    private static async Task<T> QuerySingleAsync<T>(string sql, object parameters)
    {
        await using var connection = TestDatabase.CreateConnection(IdentityDatabase.ConnectionString);
        return await connection.QuerySingleAsync<T>(sql, parameters);
    }
}
