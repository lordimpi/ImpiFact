using System.Net;
using System.Text;
using System.Text.Json;

namespace ImpiFact.Integration.Tests.Identity;

/// <summary>
/// Un cuerpo JSON (JavaScript Object Notation) mal formado o con un campo de tipo equivocado no es un error del
/// servidor: la API responde 422 con el mismo ProblemDetails de los errores de validación, en cualquier ambiente.
/// </summary>
[TestFixture]
public sealed class InvalidBodyTests
{
    [TestCase("Development", """{"email": 5, "password": "x"}""")]
    [TestCase("Production", """{"email": 5, "password": "x"}""")]
    [TestCase("Development", """{"email": "a@b.co", """)]
    [TestCase("Production", """{"email": "a@b.co", """)]
    public async Task A_body_that_cannot_be_read_answers_422_with_the_validation_shape(string environment, string body)
    {
        using var factory = new IdentityApiFactory(environment);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/v1/auth/login", new StringContent(body, Encoding.UTF8, "application/json"));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
            Assert.That(root.GetProperty("code").GetString(), Is.EqualTo("request.invalid_body"));
            Assert.That(root.GetProperty("success").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("message").GetString(), Does.StartWith("La solicitud no se pudo leer"));
            Assert.That(root.TryGetProperty("correlation_id", out _), Is.True);
        });
    }
}
