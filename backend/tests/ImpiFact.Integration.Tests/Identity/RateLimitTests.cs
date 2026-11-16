using System.Net;
using System.Text.Json;
using ImpiFact.Application.Abstractions.Identity;

namespace ImpiFact.Integration.Tests.Identity;

/// <summary>
/// Límite de solicitudes por minuto por API key y por IP (Arquitectura detallada, secciones 9.1 y 11). Los
/// límites se bajan por configuración para no tener que hacer cientos de solicitudes.
/// </summary>
[TestFixture]
public sealed class RateLimitTests
{
    [Test]
    public async Task A_key_over_its_per_minute_limit_gets_429_while_another_key_still_passes()
    {
        using var factory = new IdentityApiFactory(settings: new Dictionary<string, string?>
        {
            ["RateLimit:PermitPerMinutePerKey"] = "2",
        });
        var (busyKey, _) = factory.AddApiKey(Guid.CreateVersion7(), [ApiScopes.DocumentsRead]);
        var (otherKey, _) = factory.AddApiKey(Guid.CreateVersion7(), [ApiScopes.DocumentsRead]);
        using var busy = factory.CreateClient(busyKey);
        using var other = factory.CreateClient(otherKey);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var response = await busy.GetAsync("/test/whoami");
            statuses.Add(response.StatusCode);
        }

        using var rejected = await busy.GetAsync("/test/whoami");
        using var body = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        using var otherResponse = await other.GetAsync("/test/whoami");

        Assert.Multiple(() =>
        {
            Assert.That(statuses, Is.EqualTo(new[] { HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests }));
            Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(body.RootElement.GetProperty("success").GetBoolean(), Is.False);
            Assert.That(otherResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });
    }

    [Test]
    public async Task Requests_from_one_ip_over_its_per_minute_limit_get_429()
    {
        using var factory = new IdentityApiFactory(settings: new Dictionary<string, string?>
        {
            ["RateLimit:PermitPerMinutePerIp"] = "2",
        });
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var response = await client.GetAsync("/test/ok");
            statuses.Add(response.StatusCode);
        }

        Assert.That(statuses, Is.EqualTo(new[] { HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests }));
    }

    [Test]
    public async Task The_per_ip_limit_is_checked_first_so_requests_with_an_exhausted_key_still_count_against_the_ip()
    {
        using var factory = new IdentityApiFactory(settings: new Dictionary<string, string?>
        {
            ["RateLimit:PermitPerMinutePerKey"] = "1",
            ["RateLimit:PermitPerMinutePerIp"] = "2",
        });
        var (key, _) = factory.AddApiKey(Guid.CreateVersion7(), [ApiScopes.DocumentsRead]);
        using var withKey = factory.CreateClient(key);
        using var anonymous = factory.CreateClient();

        using var first = await withKey.GetAsync("/test/whoami");
        using var keyExhausted = await withKey.GetAsync("/test/whoami");
        using var ipExhausted = await anonymous.GetAsync("/test/ok");

        Assert.Multiple(() =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(keyExhausted.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(ipExhausted.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        });
    }

    [Test]
    public async Task A_bearer_token_longer_than_any_api_key_gets_no_per_key_partition()
    {
        using var factory = new IdentityApiFactory(settings: new Dictionary<string, string?>
        {
            ["RateLimit:PermitPerMinutePerKey"] = "1",
        });
        using var client = factory.CreateClient("apk_" + new string('a', ApiKeyAuthenticatorMaxLength));

        using var first = await client.GetAsync("/test/ok");
        using var second = await client.GetAsync("/test/ok");

        Assert.Multiple(() =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK), "An over-long token must not be limited as if it were a key.");
        });
    }

    private const int ApiKeyAuthenticatorMaxLength = ImpiFact.Infrastructure.Identity.ApiKeyAuthenticator.MaxLength;
}
