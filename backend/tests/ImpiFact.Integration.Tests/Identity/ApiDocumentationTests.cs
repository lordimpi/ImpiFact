using System.Net;

namespace ImpiFact.Integration.Tests.Identity;

/// <summary>
/// La documentación interactiva (Scalar en <c>/scalar</c>, sobre el documento OpenAPI) solo se publica en
/// desarrollo y en staging (Arquitectura detallada, sección 9.3).
/// </summary>
[TestFixture]
public sealed class ApiDocumentationTests
{
    [TestCase("Development")]
    [TestCase("Staging")]
    public async Task Scalar_and_the_openapi_document_are_published_in_development_and_staging(string environment)
    {
        using var factory = new IdentityApiFactory(environment);
        using var client = factory.CreateClient();

        using var scalar = await client.GetAsync("/scalar");
        using var openApi = await client.GetAsync("/openapi/v1.json");

        Assert.Multiple(() =>
        {
            Assert.That(scalar.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(scalar.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
            Assert.That(openApi.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });
    }

    [Test]
    public async Task Scalar_loads_its_configuration_module_with_the_halloween_theme()
    {
        using var factory = new IdentityApiFactory("Development");
        using var client = factory.CreateClient();

        var page = await client.GetStringAsync("/scalar");
        using var module = await client.GetAsync("/scalar-config.js");

        Assert.Multiple(async () =>
        {
            Assert.That(page, Does.Contain("scalar-config.js"));
            Assert.That(module.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(module.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/javascript"));
            Assert.That(await module.Content.ReadAsStringAsync(), Does.Contain("theme: 'halloween'"));
        });
    }

    [Test]
    public async Task Scalar_and_the_openapi_document_do_not_exist_in_production()
    {
        using var factory = new IdentityApiFactory("Production");
        var (key, _) = factory.AddApiKey(Guid.CreateVersion7(), ["documents:read"]);
        using var anonymous = factory.CreateClient();
        using var authenticated = factory.CreateClient(key);

        using var anonymousScalar = await anonymous.GetAsync("/scalar");
        using var authenticatedScalar = await authenticated.GetAsync("/scalar");
        using var authenticatedOpenApi = await authenticated.GetAsync("/openapi/v1.json");

        Assert.Multiple(() =>
        {
            Assert.That(anonymousScalar.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(authenticatedScalar.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(authenticatedOpenApi.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }
}
