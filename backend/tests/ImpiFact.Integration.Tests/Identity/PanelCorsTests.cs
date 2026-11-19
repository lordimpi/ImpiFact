using System.Net;
using ImpiFact.Infrastructure.Options;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace ImpiFact.Integration.Tests.Identity;

/// <summary>
/// CORS (Cross-Origin Resource Sharing: permiso para que el panel, servido desde otro origen, llame a la API desde
/// el navegador) con credenciales, solo para los orígenes de <c>Cors:Origins</c>; y la cookie de sesión del panel,
/// que fuera de Development siempre lleva <c>Secure</c>. No abre la base de datos.
/// </summary>
[TestFixture]
public sealed class PanelCorsTests
{
    private const string PanelOrigin = "https://panel.example.com";

    [TestCase("/api/v1/auth/refresh")]
    [TestCase("/api/v1/auth/logout")]
    public async Task A_preflight_from_the_configured_panel_origin_allows_credentials_and_the_panel_header(string path)
    {
        using var factory = new IdentityApiFactory(settings: new Dictionary<string, string?> { ["Cors:Origins:0"] = PanelOrigin });

        using var response = await factory.CreateClient().SendAsync(Preflight(path, PanelOrigin));

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(Header(response, HeaderNames.AccessControlAllowOrigin), Is.EqualTo(PanelOrigin));
            Assert.That(Header(response, HeaderNames.AccessControlAllowCredentials), Is.EqualTo("true"));
            Assert.That(Header(response, HeaderNames.AccessControlAllowHeaders), Does.Contain("x-client").IgnoreCase);
        });
    }

    [Test]
    public async Task A_preflight_from_another_origin_gets_no_cors_permission()
    {
        using var factory = new IdentityApiFactory(settings: new Dictionary<string, string?> { ["Cors:Origins:0"] = PanelOrigin });

        using var response = await factory.CreateClient().SendAsync(Preflight("/api/v1/auth/refresh", "https://otro-sitio.example.com"));

        Assert.Multiple(() =>
        {
            Assert.That(response.Headers.Contains(HeaderNames.AccessControlAllowOrigin), Is.False);
            Assert.That(response.Headers.Contains(HeaderNames.AccessControlAllowCredentials), Is.False);
        });
    }

    [Test]
    public async Task Without_configured_origins_no_origin_gets_cors_permission()
    {
        using var factory = new IdentityApiFactory();

        using var response = await factory.CreateClient().SendAsync(Preflight("/api/v1/auth/refresh", PanelOrigin));

        Assert.Multiple(() =>
        {
            Assert.That(response.Headers.Contains(HeaderNames.AccessControlAllowOrigin), Is.False);
            Assert.That(response.Headers.Contains(HeaderNames.AccessControlAllowCredentials), Is.False);
        });
    }

    [TestCase("*")]
    [TestCase("https://panel.example.com/login")]
    [TestCase("panel.example.com")]
    public void A_wildcard_or_malformed_origin_stops_startup(string origin)
    {
        using var factory = new IdentityApiFactory(settings: new Dictionary<string, string?> { ["Cors:Origins:0"] = origin });

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.That(exception?.Message, Does.Contain($"{CorsOriginsOptions.SectionName}:{nameof(CorsOriginsOptions.Origins)}:0"));
    }

    [Test]
    public async Task Outside_development_the_panel_cookie_is_secure_even_over_plain_http()
    {
        using var factory = new IdentityApiFactory("Production");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost"), HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        request.Headers.Add("X-Client", "panel");

        using var response = await client.SendAsync(request);
        var cookie = SetCookieHeaderValue.ParseList(response.Headers.GetValues(HeaderNames.SetCookie).ToList())
            .Single(candidate => candidate.Name.Value == "impifact_refresh");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(cookie.Secure, Is.True);
            Assert.That(cookie.HttpOnly, Is.True);
            Assert.That(cookie.SameSite, Is.EqualTo(SameSiteMode.Strict));
        });
    }

    private static HttpRequestMessage Preflight(string path, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add(HeaderNames.Origin, origin);
        request.Headers.Add(HeaderNames.AccessControlRequestMethod, "POST");
        request.Headers.Add(HeaderNames.AccessControlRequestHeaders, "content-type,x-client");
        return request;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
}
