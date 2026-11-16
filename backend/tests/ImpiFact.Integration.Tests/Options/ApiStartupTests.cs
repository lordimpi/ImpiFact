using System.Net;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Infrastructure.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ImpiFact.Integration.Tests.Options;

/// <summary>
/// Arranca la API real en memoria (WebApplicationFactory) con su <c>appsettings.json</c>. La cadena de conexión
/// es un secreto: no está en <c>appsettings.json</c> y, si no llega por otra fuente, la API no arranca. No abre
/// conexiones a la base de datos, por eso no necesita Docker.
/// </summary>
[TestFixture]
public sealed class ApiStartupTests
{
    [Test]
    public void The_api_does_not_start_without_a_connection_string()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("Database:ConnectionString", ""));

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.That(exception?.Message, Does.Contain(nameof(DatabaseOptions.ConnectionString)));
    }

    [Test]
    public async Task With_a_connection_string_the_api_starts_with_the_appsettings_defaults()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("Database:ConnectionString", "Host=localhost;Database=impifact"));

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/live");
        var dian = factory.Services.GetRequiredService<IOptionsService<DianOptions>>().Value;
        var jwt = factory.Services.GetRequiredService<IOptionsService<JwtOptions>>().Value;
        var apiKey = factory.Services.GetRequiredService<IOptionsService<ApiKeyOptions>>().Value;
        var secrets = factory.Services.GetRequiredService<IOptionsService<SecretsOptions>>().Value;
        using var scope = factory.Services.CreateScope();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(dian.HabilitacionUrl, Is.EqualTo("https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc"));
            Assert.That(dian.ProduccionUrl, Is.EqualTo("https://vpfe.dian.gov.co/WcfDianCustomerServices.svc"));
            Assert.That(dian.HabilitacionQrUrl, Is.EqualTo("https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey="));
            Assert.That(dian.ProduccionQrUrl, Is.EqualTo("https://catalogo-vpfe.dian.gov.co/document/searchqr?documentkey="));
            Assert.That(dian.TimeoutSeconds, Is.EqualTo(60));
            Assert.That(jwt.Issuer, Is.EqualTo("impifact"));
            Assert.That(jwt.Audience, Is.EqualTo("impifact-web"));
            Assert.That(jwt.AccessMinutes, Is.EqualTo(15));
            Assert.That(jwt.RefreshDays, Is.EqualTo(14));
            Assert.That(apiKey.Prefix, Is.EqualTo("apk_"));
            Assert.That(secrets.Provider, Is.EqualTo("LocalAesGcm"));
            Assert.That(scope.ServiceProvider.GetService<IUnitOfWork>(), Is.Not.Null);
            Assert.That(scope.ServiceProvider.GetService<IReadConnectionFactory>(), Is.Not.Null);
        });
    }
}
