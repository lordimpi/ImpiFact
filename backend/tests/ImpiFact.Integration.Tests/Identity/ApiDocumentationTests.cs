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
