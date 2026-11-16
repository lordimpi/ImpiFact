using System.Net;
using System.Text.Json;
using ImpiFact.Application.Abstractions.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Identity;

/// <summary>
/// Si la base de datos no responde al buscar una API key, la API contesta 503 (servicio no disponible, con
/// <c>Retry-After</c>) y no 500: el cliente puede reintentar. La respuesta no muestra detalles internos (ni el
/// motor, ni el servidor, ni la excepción) y el registro no incluye la llave.
/// </summary>
[TestFixture]
public sealed class ApiKeyStoreUnavailableTests
{
    [Test]
    public async Task An_api_key_lookup_against_an_unavailable_database_answers_503_without_details()
    {
        using var factory = new IdentityApiFactory(withRealApiKeyStore: true);
        var key = factory.Services.GetRequiredService<IApiKeyGenerator>().Generate().PlainText;
        using var client = factory.CreateClient(key);

        using var response = await client.GetAsync("/test/whoami");
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(response.Headers.RetryAfter?.Delta, Is.Not.Null);
            Assert.That(body.RootElement.GetProperty("success").GetBoolean(), Is.False);
            Assert.That(body.RootElement.GetProperty("code").GetString(), Is.EqualTo("identity.store_unavailable"));
            Assert.That(text, Does.Not.Contain("Npgsql").And.Not.Contain("127.0.0.1").And.Not.Contain("Exception"));
            Assert.That(factory.Logs.Entries, Has.None.Contains(key));
        });
    }
}
