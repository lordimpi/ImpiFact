using System.Net;
using ImpiFact.Application.Abstractions.Identity;

namespace ImpiFact.Integration.Tests.Identity;

/// <summary>
/// Políticas de autorización por alcance (scope) de la API key: <c>documents:write</c>, <c>documents:read</c>,
/// <c>config:write</c> y <c>platform:*</c> (Arquitectura detallada, sección 9.2).
/// </summary>
[TestFixture]
public sealed class ScopePolicyTests
{
    private IdentityApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartApi() => _factory = new IdentityApiFactory();

    [OneTimeTearDown]
    public void StopApi() => _factory.Dispose();

    [TestCase(ApiScopes.DocumentsWrite, "/test/documents-write")]
    [TestCase(ApiScopes.DocumentsRead, "/test/documents-read")]
    [TestCase(ApiScopes.ConfigWrite, "/test/config-write")]
    public async Task A_company_key_with_the_scope_is_allowed(string scope, string path)
    {
        var (key, _) = _factory.AddApiKey(Guid.CreateVersion7(), [scope]);
        using var client = _factory.CreateClient(key);

        using var response = await client.GetAsync(path);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [TestCase(ApiScopes.DocumentsRead, "/test/documents-write")]
    [TestCase(ApiScopes.DocumentsWrite, "/test/documents-read")]
    [TestCase(ApiScopes.DocumentsWrite, "/test/config-write")]
    [TestCase(ApiScopes.ConfigWrite, "/test/platform")]
    public async Task A_key_without_the_scope_is_forbidden_with_403(string scope, string path)
    {
        var (key, _) = _factory.AddApiKey(Guid.CreateVersion7(), [scope]);
        using var client = _factory.CreateClient(key);

        using var response = await client.GetAsync(path);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task A_platform_key_is_allowed_on_platform_endpoints()
    {
        var (key, _) = _factory.AddApiKey(null, [ApiScopes.Platform]);
        using var client = _factory.CreateClient(key);

        using var response = await client.GetAsync("/test/platform");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task A_company_key_never_acts_as_platform_even_with_the_platform_scope()
    {
        var (key, _) = _factory.AddApiKey(Guid.CreateVersion7(), [ApiScopes.Platform]);
        using var client = _factory.CreateClient(key);

        using var response = await client.GetAsync("/test/platform");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task A_key_with_several_scopes_gets_each_of_them()
    {
        var (key, _) = _factory.AddApiKey(Guid.CreateVersion7(), [ApiScopes.DocumentsRead, ApiScopes.DocumentsWrite]);
        using var client = _factory.CreateClient(key);

        using var read = await client.GetAsync("/test/documents-read");
        using var write = await client.GetAsync("/test/documents-write");
        using var config = await client.GetAsync("/test/config-write");

        Assert.Multiple(() =>
        {
            Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(write.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(config.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        });
    }

    [Test]
    public async Task Without_credentials_a_protected_endpoint_answers_401()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/test/documents-read");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
