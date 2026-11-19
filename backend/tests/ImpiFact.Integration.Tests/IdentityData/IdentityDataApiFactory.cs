using System.Net.Http.Headers;
using System.Security.Cryptography;
using ImpiFact.Api.Endpoints;
using ImpiFact.Integration.Tests.Identity;
using ImpiFact.Integration.Tests.Protection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Integration.Tests.IdentityData;

/// <summary>
/// Levanta la API real en memoria (WebApplicationFactory) contra el PostgreSQL de <see cref="IdentityDatabase"/>, con
/// los adaptadores reales de Identity. Agrega los endpoints de prueba (por ejemplo <c>/test/whoami</c>) y captura los
/// registros para revisar que no filtren credenciales.
/// </summary>
/// <param name="configureServices">Cambia servicios de esta API, por ejemplo los plazos de la sesión.</param>
internal sealed class IdentityDataApiFactory(Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
{
    private readonly ECDsa _signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ProtectionScratch _protection = new();

    public CapturingLoggerProvider Logs { get; } = new();

    public HttpClient CreateClient(string bearer)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Database:ConnectionString", IdentityDatabase.ConnectionString);
        builder.UseSetting("Secrets:LocalAesGcm:MasterKeyFile", _protection.MasterKeyFile);
        builder.UseSetting("Secrets:LocalAesGcm:CertificateDirectory", _protection.CertificateDirectory);
        builder.UseSetting("Storage:Local:Root", Path.Combine(_protection.Root, "storage"));
        builder.UseSetting("Jwt:SigningKey", _signingKey.ExportPkcs8PrivateKeyPem());
        builder.UseSetting("Logging:LogLevel:Default", "Trace");
        builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore", "Trace");
        // El límite de inicio de sesión real (10 por minuto) cortaría las pruebas, que inician muchas sesiones.
        builder.UseSetting("RateLimit:LoginPerMinute", "10000");
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IEndpointModule, TestEndpoints>();
            configureServices?.Invoke(services);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _signingKey.Dispose();
            _protection.Dispose();
        }
    }
}
