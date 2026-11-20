using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Security.KeyVault.Keys;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using ImpiFact.Infrastructure.Protection;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Protection.KeyVault;

/// <summary>
/// lowkey-vault (un emulador de código abierto de Azure Key Vault, licencia MIT) desechable en Docker para probar el
/// proveedor <c>KeyVault</c> contra un servicio real, y una base del motor de <see cref="TestDatabase"/> para las llaves
/// de Data Protection. El emulador guarda todo en memoria; la llave RSA que envuelve las llaves de Data Protection la
/// crea esta clase, como lo haría el despliegue.
/// </summary>
/// <remarks>
/// El emulador atiende por https con un certificado propio: las pruebas lo descargan del emulador y aceptan solo ese
/// (no cualquiera). Pide que la solicitud lleve un token, pero no revisa su valor, así que la credencial es fija
/// (<see cref="StaticToken"/>); ambas cosas llegan a la aplicación por <see cref="KeyVaultTestClient"/>, nunca por
/// configuración. Con <c>LOWKEY_VAULT_RELAXED_PORTS</c> el vault por omisión (<c>localhost</c>) responde en el puerto
/// que Docker eligió.
/// </remarks>
[SetUpFixture]
[Category("Integration")]
public sealed class LowkeyVault
{
    public const string Image = "nagyesta/lowkey-vault:7.4.0";

    public const string KeyName = "impifact-data-protection";

    /// <summary>Otra llave del mismo vault, para comprobar que una llave no abre lo que envolvió otra.</summary>
    public const string OtherKeyName = "impifact-data-protection-other";

    private const int HttpsPort = 8443;
    private const int MetadataPort = 8080;

    private static IContainer? container;
    private static TestDatabaseServer? database;
    private static X509Certificate2? serverCertificate;

    public static string VaultUri { get; private set; } = "";

    /// <summary>La identidad administrada simulada del emulador (el protocolo de App Service: IDENTITY_ENDPOINT).</summary>
    public static string IdentityEndpoint { get; private set; } = "";

    /// <summary>Credencial y transporte que la aplicación usa contra el emulador.</summary>
    internal static KeyVaultTestClient TestClient { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        container = new ContainerBuilder(Image)
            .WithEnvironment("LOWKEY_ARGS", "--LOWKEY_VAULT_RELAXED_PORTS=true")
            .WithPortBinding(HttpsPort, true)
            .WithPortBinding(MetadataPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(MetadataPort).ForPath("/ping")))
            .Build();
        database = TestDatabase.CreateServer();
        await Task.WhenAll(container.StartAsync(), database.StartAsync());

        VaultUri = $"https://localhost:{container.GetMappedPublicPort(HttpsPort)}";
        IdentityEndpoint = $"http://127.0.0.1:{container.GetMappedPublicPort(MetadataPort)}/metadata/identity/oauth2/token";
        serverCertificate = await DownloadServerCertificateAsync();
        TestClient = new KeyVaultTestClient(new StaticToken(), new HttpClientTransport(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                certificate is not null && certificate.GetCertHash(HashAlgorithmName.SHA256).SequenceEqual(serverCertificate.GetCertHash(HashAlgorithmName.SHA256)),
        }));

        var keys = new KeyClient(new Uri(VaultUri), TestClient.Credential!, Options(new KeyClientOptions()));
        // Como lo haría el despliegue: una llave RSA para envolver y desenvolver. lowkey-vault revisa además la operación
        // «decrypt» al desenvolver (Azure solo revisa «unwrapKey»), así que aquí también se asigna.
        foreach (var name in new[] { KeyName, OtherKeyName })
        {
            var key = new CreateRsaKeyOptions(name) { KeySize = 2048 };
            key.KeyOperations.Add(KeyOperation.WrapKey);
            key.KeyOperations.Add(KeyOperation.UnwrapKey);
            key.KeyOperations.Add(KeyOperation.Decrypt);
            await keys.CreateRsaKeyAsync(key);
        }
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        serverCertificate?.Dispose();
        if (container is not null)
        {
            await container.DisposeAsync();
        }

        if (database is not null)
        {
            await database.DisposeAsync();
        }
    }

    /// <summary>Configuración del proveedor <c>KeyVault</c> contra el emulador.</summary>
    public static Dictionary<string, string?> Settings(string keyName = KeyName) => new()
    {
        ["Secrets:Provider"] = "KeyVault",
        ["Secrets:KeyVault:VaultUri"] = VaultUri,
        ["Secrets:KeyVault:KeyName"] = keyName,
    };

    /// <summary>Servicios como los de la API con el proveedor <c>KeyVault</c>, y una base propia si se pasa su cadena.</summary>
    public static ServiceProvider Build(Dictionary<string, string?>? secrets = null, string? connectionString = null) =>
        ProtectionServices.Build(
            ProtectionServices.ValidSettings(secrets ?? Settings(), connectionString),
            extra: services => services.AddSingleton(TestClient));

    /// <summary>Una base nueva con la tabla <c>data_protection_keys</c>, para las llaves de una prueba.</summary>
    public static Task<string> CreateKeysDatabaseAsync() =>
        CloudKeysDatabase.CreateAsync(database ?? throw new InvalidOperationException("The database server is not running."));

    /// <summary>Opciones de un cliente del SDK para el emulador (sin revisar el dominio del desafío, con el transporte de prueba).</summary>
    public static T Options<T>(T options)
        where T : ClientOptions
    {
        options.Transport = TestClient.Transport;
        switch (options)
        {
            case KeyClientOptions keys:
                keys.DisableChallengeResourceVerification = true;
                break;
            case Azure.Security.KeyVault.Secrets.SecretClientOptions secrets:
                secrets.DisableChallengeResourceVerification = true;
                break;
        }

        return options;
    }

    private static async Task<X509Certificate2> DownloadServerCertificateAsync()
    {
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{container!.GetMappedPublicPort(MetadataPort)}") };
        var store = await http.GetByteArrayAsync("/metadata/default-cert/lowkey-vault.p12");
        var password = (await http.GetStringAsync("/metadata/default-cert/password")).Trim();
        return X509CertificateLoader.LoadPkcs12(store, password, X509KeyStorageFlags.EphemeralKeySet);
    }

    /// <summary>Token fijo: el emulador exige que haya uno, pero no lo revisa.</summary>
    private sealed class StaticToken : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("lowkey-vault-test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}
