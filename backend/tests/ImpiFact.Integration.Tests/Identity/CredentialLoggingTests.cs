using System.Security.Cryptography;
using ImpiFact.Application.Abstractions.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Identity;

/// <summary>
/// Ningún registro (log) contiene el encabezado <c>Authorization</c>, una API key ni un JWT, ni siquiera con el
/// nivel de detalle más alto (Trace) y en los caminos de error (Arquitectura detallada, sección 12).
/// </summary>
[TestFixture]
public sealed class CredentialLoggingTests
{
    [Test]
    public async Task No_log_entry_contains_a_presented_credential()
    {
        using var factory = new IdentityApiFactory();
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (validKey, _) = factory.AddApiKey(Guid.CreateVersion7(), [ApiScopes.DocumentsRead]);
        var (revokedKey, _) = factory.AddApiKey(Guid.CreateVersion7(), [ApiScopes.DocumentsRead], revokedAt: DateTimeOffset.UtcNow);
        var unknownKey = factory.Services.GetRequiredService<IApiKeyGenerator>().Generate().PlainText;
        var validJwt = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(Guid.CreateVersion7(), ["owner"], Guid.CreateVersion7()).Value;
        var expiredJwt = TestTokens.Sign(factory.SigningKey, issuedAt: DateTimeOffset.UtcNow.AddHours(-1));
        var forgedJwt = TestTokens.Sign(otherKey, keyId: TestTokens.KeyId(factory.SigningKey));
        string[] credentials = [validKey, revokedKey, unknownKey, validJwt, expiredJwt, forgedJwt, "garbage-token"];

        foreach (var credential in credentials)
        {
            using var client = factory.CreateClient(credential);
            using var whoAmI = await client.GetAsync("/test/whoami");
            using var forbidden = await client.GetAsync("/test/platform");
            using var failure = await client.GetAsync("/test/throw");
        }

        Assert.Multiple(() =>
        {
            // Prueba de que se capturaron los caminos de fallo de ambos esquemas; sin eso la prueba no probaría nada.
            Assert.That(factory.Logs.Entries, Has.Some.Contains("ApiKey was not authenticated"));
            Assert.That(factory.Logs.Entries, Has.Some.Contains("Jwt was not authenticated"));
            Assert.That(factory.Logs.Entries, Has.Some.Contains("Rejected revoked API key"));
            foreach (var credential in credentials)
            {
                Assert.That(factory.Logs.Entries, Has.None.Contains(credential), $"A credential starting with '{credential[..6]}' was logged.");
            }

            Assert.That(factory.Logs.Entries, Has.None.Contains("Bearer ey").And.None.Contains("Bearer apk_"));
        });
    }
}
