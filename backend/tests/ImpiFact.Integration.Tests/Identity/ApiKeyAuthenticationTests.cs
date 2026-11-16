using System.Net;
using System.Net.Http.Json;
using ImpiFact.Application.Abstractions.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Identity;

/// <summary>
/// Autenticación con API key por el encabezado <c>Authorization: Bearer apk_...</c> (Arquitectura detallada,
/// sección 9.2): búsqueda por hash, llaves revocadas o vencidas, llaves de plataforma, caché de 60 segundos con
/// limpieza al revocar y registro de <c>last_used_at</c> sin bloquear la solicitud.
/// </summary>
[TestFixture]
public sealed class ApiKeyAuthenticationTests
{
    private IdentityApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartApi() => _factory = new IdentityApiFactory();

    [OneTimeTearDown]
    public void StopApi() => _factory.Dispose();

    [Test]
    public async Task A_valid_company_key_authenticates_and_fills_the_tenant_context()
    {
        var companyId = Guid.CreateVersion7();
        var (key, _) = _factory.AddApiKey(companyId, [ApiScopes.DocumentsWrite]);
        using var client = _factory.CreateClient(key);

        using var response = await client.GetAsync("/test/whoami");
        var whoAmI = await response.Content.ReadFromJsonAsync<TestEndpoints.WhoAmI>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(whoAmI, Is.EqualTo(new TestEndpoints.WhoAmI(companyId, null, "ApiKey")));
        });
    }

    [Test]
    public async Task A_platform_key_has_no_company_and_acts_as_platform()
    {
        var (key, _) = _factory.AddApiKey(null, [ApiScopes.Platform]);
        using var client = _factory.CreateClient(key);

        var whoAmI = await client.GetFromJsonAsync<TestEndpoints.WhoAmI>("/test/whoami");

        Assert.That(whoAmI, Is.EqualTo(new TestEndpoints.WhoAmI(null, null, "Platform")));
    }

    [Test]
    public async Task An_unknown_key_is_rejected_with_401()
    {
        var unknown = _factory.Services.GetRequiredService<IApiKeyGenerator>().Generate().PlainText;
        using var client = _factory.CreateClient(unknown);

        using var response = await client.GetAsync("/test/whoami");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task A_revoked_key_is_rejected_with_401()
    {
        var (key, _) = _factory.AddApiKey(Guid.CreateVersion7(), [ApiScopes.DocumentsRead], revokedAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        using var client = _factory.CreateClient(key);

        using var response = await client.GetAsync("/test/whoami");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task An_expired_key_is_rejected_with_401()
    {
        var (key, _) = _factory.AddApiKey(Guid.CreateVersion7(), [ApiScopes.DocumentsRead], expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        using var client = _factory.CreateClient(key);

        using var response = await client.GetAsync("/test/whoami");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task A_key_that_expires_in_the_future_is_accepted()
    {
        var (key, _) = _factory.AddApiKey(Guid.CreateVersion7(), [ApiScopes.DocumentsRead], expiresAt: DateTimeOffset.UtcNow.AddDays(1));
        using var client = _factory.CreateClient(key);

        using var response = await client.GetAsync("/test/whoami");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task The_lookup_is_cached_and_revocation_clears_the_cached_entry()
    {
        var companyId = Guid.CreateVersion7();
        var (key, record) = _factory.AddApiKey(companyId, [ApiScopes.DocumentsRead]);
        using var client = _factory.CreateClient(key);

        using var first = await client.GetAsync("/test/whoami");
        var lookupsAfterFirst = _factory.ApiKeys.Lookups;
        using var second = await client.GetAsync("/test/whoami");
        var lookupsAfterSecond = _factory.ApiKeys.Lookups;

        // Revocada en la base pero todavía en caché: sigue sirviendo hasta que se limpia la entrada.
        _factory.ApiKeys.Revoke(record.Id, DateTimeOffset.UtcNow);
        using var stillCached = await client.GetAsync("/test/whoami");
        await _factory.Services.GetRequiredService<IApiKeyCacheInvalidator>().InvalidateAsync(record.KeyHash, CancellationToken.None);
        using var afterInvalidation = await client.GetAsync("/test/whoami");

        Assert.Multiple(() =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(lookupsAfterSecond, Is.EqualTo(lookupsAfterFirst), "The second request must be served from the cache.");
            Assert.That(stillCached.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(afterInvalidation.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        });
    }

    [Test]
    public async Task Unknown_keys_are_not_cached()
    {
        var unknown = _factory.Services.GetRequiredService<IApiKeyGenerator>().Generate().PlainText;
        using var client = _factory.CreateClient(unknown);

        using var first = await client.GetAsync("/test/whoami");
        var lookupsAfterFirst = _factory.ApiKeys.Lookups;
        using var second = await client.GetAsync("/test/whoami");

        Assert.That(_factory.ApiKeys.Lookups, Is.EqualTo(lookupsAfterFirst + 1));
    }

    [Test]
    public async Task Last_used_at_is_recorded_after_the_response_without_blocking_it()
    {
        var companyId = Guid.CreateVersion7();
        var (key, record) = _factory.AddApiKey(companyId, [ApiScopes.DocumentsRead]);
        using var client = _factory.CreateClient(key);
        var before = DateTimeOffset.UtcNow;

        using var response = await client.GetAsync("/test/whoami");
        var recorded = await WaitForUseAsync(record.Id);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(recorded, Is.Not.Null, "last_used_at was never recorded.");
            Assert.That(recorded, Is.GreaterThanOrEqualTo(before.AddSeconds(-1)));
        });
    }

    [Test]
    public async Task A_bearer_token_without_the_key_prefix_is_not_treated_as_an_api_key()
    {
        var lookupsBefore = _factory.ApiKeys.Lookups;
        using var client = _factory.CreateClient("not-an-api-key");

        using var response = await client.GetAsync("/test/whoami");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(_factory.ApiKeys.Lookups, Is.EqualTo(lookupsBefore), "A JWT-shaped token must never reach the API key store.");
        });
    }

    private async Task<DateTimeOffset?> WaitForUseAsync(Guid apiKeyId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var use = _factory.ApiKeys.Uses.FirstOrDefault(candidate => candidate.ApiKeyId == apiKeyId);
            if (use != default)
            {
                return use.UsedAt;
            }

            await Task.Delay(50);
        }

        return null;
    }
}
