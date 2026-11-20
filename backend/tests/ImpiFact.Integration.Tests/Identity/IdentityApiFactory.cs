using System.Net.Http.Headers;
using System.Security.Cryptography;
using ImpiFact.Api.Endpoints;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Integration.Tests.Protection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Integration.Tests.Identity;

/// <summary>
/// Levanta la API real en memoria (WebApplicationFactory) con dobles de prueba para los puertos que todavía no
/// tienen tabla: el almacén de API keys es una lista en memoria. La llave de firma de los JWT (JSON Web Token:
/// credencial firmada del panel) es una llave ECDSA (firma con curvas elípticas) P-256 creada para cada fábrica,
/// así las pruebas pueden firmar tokens propios. Los registros se capturan para revisar que no filtren secretos.
/// No abre conexiones a la base de datos, por eso no necesita Docker.
/// </summary>
internal sealed class IdentityApiFactory : WebApplicationFactory<Program>
{
    private readonly string _environment;
    private readonly bool _withSigningKey;
    private readonly IReadOnlyDictionary<string, string?> _settings;
    private readonly bool _withRealApiKeyStore;
    private readonly Action<IServiceCollection>? _configureServices;

    /// <param name="withRealApiKeyStore">
    /// Deja el almacén de API keys real (sobre la tabla), que apunta a una base inalcanzable: sirve para probar qué
    /// responde la API cuando la base no está disponible.
    /// </param>
    /// <param name="configureServices">Servicios de prueba adicionales, después de los de esta fábrica.</param>
    public IdentityApiFactory(
        string environment = "Development",
        bool withSigningKey = true,
        IReadOnlyDictionary<string, string?>? settings = null,
        bool withRealApiKeyStore = false,
        Action<IServiceCollection>? configureServices = null)
    {
        _environment = environment;
        _withSigningKey = withSigningKey;
        _settings = settings ?? new Dictionary<string, string?>();
        _withRealApiKeyStore = withRealApiKeyStore;
        _configureServices = configureServices;
    }

    /// <summary>Llave privada con la que la API firma los JWT en esta fábrica.</summary>
    public ECDsa SigningKey { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public InMemoryApiKeyStore ApiKeys { get; } = new();

    /// <summary>Llave maestra y carpeta de certificados temporales: la API no arranca sin ellas.</summary>
    private ProtectionScratch Protection { get; } = new();

    public CapturingLoggerProvider Logs { get; } = new();

    /// <summary>
    /// Crea una API key con el generador real, la guarda en el almacén en memoria y devuelve el texto claro (lo
    /// que el cliente envía) junto con el registro guardado.
    /// </summary>
    public (string PlainText, ApiKeyRecord Record) AddApiKey(
        Guid? companyId, string[] scopes, DateTimeOffset? expiresAt = null, DateTimeOffset? revokedAt = null)
    {
        var generated = Services.GetRequiredService<IApiKeyGenerator>().Generate();
        var record = new ApiKeyRecord(Guid.CreateVersion7(), companyId, generated.Hash, scopes, expiresAt, revokedAt);
        ApiKeys.Add(record);
        return (generated.PlainText, record);
    }

    public HttpClient CreateClient(string bearer)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting("Database:ConnectionString", UnreachableDatabase.ConnectionString);
        builder.UseSetting("Secrets:LocalAesGcm:MasterKeyFile", Protection.MasterKeyFile);
        builder.UseSetting("Secrets:LocalAesGcm:CertificateDirectory", Protection.CertificateDirectory);
        builder.UseSetting("Storage:Local:Root", Path.Combine(Protection.Root, "storage"));
        builder.UseSetting("Logging:LogLevel:Default", "Trace");
        builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore", "Trace");
        if (_withSigningKey)
        {
            builder.UseSetting("Jwt:SigningKey", SigningKey.ExportPkcs8PrivateKeyPem());
        }

        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureTestServices(services =>
        {
            if (!_withRealApiKeyStore)
            {
                services.AddSingleton<IApiKeyStore>(ApiKeys);
            }

            services.AddSingleton<IEndpointModule, TestEndpoints>();
            services.AddSingleton<IStartupFilter, TestRemoteAddress>();
            _configureServices?.Invoke(services);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            SigningKey.Dispose();
            Protection.Dispose();
        }
    }
}
