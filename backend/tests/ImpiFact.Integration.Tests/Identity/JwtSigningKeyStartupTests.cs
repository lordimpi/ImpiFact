using System.Net;
using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ImpiFact.Integration.Tests.Identity;

/// <summary>
/// La llave privada que firma los JWT (JSON Web Token) es un secreto: llega por configuración (variable de entorno
/// <c>Jwt__SigningKey</c> o el proveedor de secretos), nunca en <c>appsettings.json</c>. Fuera de Development la
/// API no arranca sin ella; en Development genera una llave efímera y lo avisa en el registro.
/// </summary>
[TestFixture]
public sealed class JwtSigningKeyStartupTests
{
    [TestCase("Production")]
    [TestCase("Staging")]
    public void Outside_development_the_api_does_not_start_without_a_signing_key(string environment)
    {
        using var factory = new IdentityApiFactory(environment, withSigningKey: false);

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.That(exception?.Message, Does.Contain(nameof(JwtOptions.SigningKey)));
    }

    [Test]
    public void An_invalid_signing_key_stops_startup_without_echoing_the_value()
    {
        const string marker = "c2VjcmV0LW1hcmtlcg";
        using var factory = new IdentityApiFactory("Production", withSigningKey: false, settings: new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = $"-----BEGIN PRIVATE KEY-----\n{marker}\n-----END PRIVATE KEY-----",
        });

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Multiple(() =>
        {
            Assert.That(exception?.Message, Does.Contain(nameof(JwtOptions.SigningKey)));
            Assert.That(exception?.Message, Does.Not.Contain(marker));
        });
    }

    [Test]
    public async Task In_development_an_ephemeral_key_is_generated_and_logged_as_a_warning()
    {
        using var factory = new IdentityApiFactory("Development", withSigningKey: false);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/.well-known/jwks.json");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(factory.Logs.Entries, Has.Some.Contains("Warning").And.Contains("ephemeral"));
        });
    }
}
