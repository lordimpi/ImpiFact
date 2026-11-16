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
}
