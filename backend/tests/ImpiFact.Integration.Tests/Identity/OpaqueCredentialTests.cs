using System.Security.Cryptography;
using System.Text;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Identity;

/// <summary>
/// Formato de las API keys y de los tokens de renovación (refresh tokens): valores aleatorios opacos de los que
/// solo se guarda el resumen SHA-256 en hexadecimal (64 caracteres, columna <c>char(64)</c>) y, para las API keys,
/// un prefijo corto para mostrar (<c>varchar(12)</c>). Modelo de datos detallado, sección 3.2.
/// </summary>
[TestFixture]
public sealed class OpaqueCredentialTests
{
    private IdentityApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartApi() => _factory = new IdentityApiFactory();

    [OneTimeTearDown]
    public void StopApi() => _factory.Dispose();

    [Test]
    public void An_api_key_has_the_prefix_32_random_bytes_and_only_its_hash_and_display_prefix_are_kept()
    {
        var key = _factory.Services.GetRequiredService<IApiKeyGenerator>().Generate();
        var body = key.PlainText["apk_".Length..];

        Assert.Multiple(() =>
        {
            Assert.That(key.PlainText, Does.StartWith("apk_"));
            Assert.That(body, Does.Match("^[A-Za-z0-9_-]{43}$"), "32 random bytes in URL-safe Base64 without padding.");
            Assert.That(key.Hash, Is.EqualTo(Sha256Hex(key.PlainText)));
            Assert.That(key.Hash, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(key.DisplayPrefix, Has.Length.EqualTo(12));
            Assert.That(key.PlainText, Does.StartWith(key.DisplayPrefix));
        });
    }

    [Test]
    public void Two_api_keys_are_never_equal()
    {
        var generator = _factory.Services.GetRequiredService<IApiKeyGenerator>();

        var keys = Enumerable.Range(0, 100).Select(_ => generator.Generate().PlainText).ToHashSet(StringComparer.Ordinal);

        Assert.That(keys, Has.Count.EqualTo(100));
    }

    [Test]
    public void A_refresh_token_is_random_keeps_only_its_hash_and_lasts_14_days()
    {
        var factory = _factory.Services.GetRequiredService<IRefreshTokenFactory>();
        var now = _factory.Services.GetRequiredService<IClock>().UtcNow;

        var token = factory.Create();
        var other = factory.Create();

        Assert.Multiple(() =>
        {
            Assert.That(token.Value, Does.Match("^[A-Za-z0-9_-]{43}$"));
            Assert.That(token.Value, Is.Not.EqualTo(other.Value));
            Assert.That(token.Hash, Is.EqualTo(Sha256Hex(token.Value)));
            Assert.That(factory.Hash(token.Value), Is.EqualTo(token.Hash));
            Assert.That(token.ExpiresAt - now, Is.EqualTo(TimeSpan.FromDays(14)).Within(TimeSpan.FromSeconds(5)));
        });
    }

    private static string Sha256Hex(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
