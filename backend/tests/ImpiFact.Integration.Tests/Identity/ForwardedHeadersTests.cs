using Microsoft.Extensions.Options;

namespace ImpiFact.Integration.Tests.Identity;

/// <summary>
/// Detrás de un proxy, la IP (Internet Protocol) real del cliente llega en <c>X-Forwarded-For</c>. Solo se cree ese
/// encabezado si la conexión viene de un proxy de confianza: por defecto solo la propia máquina (loopback), y cada
/// despliegue agrega los suyos en <c>ForwardedHeaders:KnownProxies</c> y <c>ForwardedHeaders:KnownNetworks</c>. Si
/// no, cualquiera podría elegir su IP y saltarse el límite de solicitudes por IP.
/// </summary>
[TestFixture]
public sealed class ForwardedHeadersTests
{
    private const string ClientIp = "203.0.113.9";

    [Test]
    public async Task By_default_a_forwarded_for_header_from_a_non_loopback_peer_is_ignored()
    {
        using var factory = new IdentityApiFactory();

        var ip = await ClientIpSeenByTheApiAsync(factory, peer: "10.0.0.5");

        Assert.That(ip, Is.EqualTo("10.0.0.5"));
    }

    [Test]
    public async Task A_configured_proxy_is_trusted()
    {
        using var factory = new IdentityApiFactory(settings: new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.5",
        });

        var ip = await ClientIpSeenByTheApiAsync(factory, peer: "10.0.0.5");

        Assert.That(ip, Is.EqualTo(ClientIp));
    }

    [Test]
    public async Task A_configured_network_is_trusted()
    {
        using var factory = new IdentityApiFactory(settings: new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/8",
        });

        var ip = await ClientIpSeenByTheApiAsync(factory, peer: "10.20.30.40");

        Assert.That(ip, Is.EqualTo(ClientIp));
    }

    [Test]
    public async Task The_loopback_peer_stays_trusted_when_other_proxies_are_configured()
    {
        using var factory = new IdentityApiFactory(settings: new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.5",
        });

        var ip = await ClientIpSeenByTheApiAsync(factory, peer: "127.0.0.1");

        Assert.That(ip, Is.EqualTo(ClientIp));
    }

    [TestCase("ForwardedHeaders:KnownProxies:0", "not-an-ip")]
    [TestCase("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/99")]
    public void An_invalid_proxy_address_stops_startup(string key, string value)
    {
        using var factory = new IdentityApiFactory(settings: new Dictionary<string, string?> { [key] = value });

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.That(exception?.Message, Does.Contain(key.Split(':')[1]));
    }

    private static async Task<string> ClientIpSeenByTheApiAsync(IdentityApiFactory factory, string peer)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/client-ip");
        request.Headers.Add(TestRemoteAddress.HeaderName, peer);
        request.Headers.Add("X-Forwarded-For", ClientIp);

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
