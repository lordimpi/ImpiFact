using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Azure.Security.KeyVault.Secrets;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.Protection;

/// <summary>
/// Clientes de Azure Key Vault del proveedor <c>KeyVault</c>, todos con la misma identidad: <see cref="DefaultAzureCredential"/>
/// (la identidad administrada en Azure), o la credencial y el transporte de <see cref="KeyVaultTestClient"/> en las pruebas
/// contra lowkey-vault.
/// </summary>
/// <remarks>
/// Antes de mandar la credencial, el SDK revisa que el desafío de autenticación del vault nombre un dominio de Key Vault;
/// un emulador (otro dominio) no lo cumple, así que la revisión solo se apaga para direcciones fuera de los dominios de
/// Azure, donde no hay un Key Vault real que proteger.
/// </remarks>
internal sealed class KeyVaultClients(IOptions<SecretsOptions> options, KeyVaultTestClient? test = null)
{
    private static readonly string[] AzureKeyVaultDomains =
        [".vault.azure.net", ".vault.azure.cn", ".vault.usgovcloudapi.net", ".vault.microsoftazure.de", ".managedhsm.azure.net"];

    private readonly Lazy<TokenCredential> credential = new(() => test?.Credential ?? new DefaultAzureCredential());

    public KeyVaultSecretsOptions Settings => options.Value.KeyVault;

    public Uri VaultUri => new(Settings.VaultUri);

    public SecretClient Secrets() => new(VaultUri, credential.Value, Configure(new SecretClientOptions()));

    public KeyClient Keys() => new(VaultUri, credential.Value, Configure(new KeyClientOptions()));

    public CryptographyClient Cryptography(Uri keyId) => new(keyId, credential.Value, Configure(new CryptographyClientOptions()));

    /// <summary>Si la dirección es de un Key Vault de Azure (y no de un emulador).</summary>
    internal static bool IsAzureKeyVault(Uri vaultUri) =>
        AzureKeyVaultDomains.Any(domain => vaultUri.Host.EndsWith(domain, StringComparison.OrdinalIgnoreCase));

    private T Configure<T>(T clientOptions)
        where T : ClientOptions
    {
        var emulator = !IsAzureKeyVault(VaultUri);
        switch (clientOptions)
        {
            case SecretClientOptions secrets:
                secrets.DisableChallengeResourceVerification = emulator;
                break;
            case KeyClientOptions keys:
                keys.DisableChallengeResourceVerification = emulator;
                break;
            case CryptographyClientOptions cryptography:
                cryptography.DisableChallengeResourceVerification = emulator;
                break;
        }

        if (test is not null)
        {
            clientOptions.Transport = test.Transport;
        }

        return clientOptions;
    }
}

/// <summary>
/// Transporte fijo para Key Vault, solo para pruebas (lowkey-vault, con su certificado propio), y una credencial fija o,
/// sin ella, <see cref="DefaultAzureCredential"/>. Si no se registra, los clientes usan el transporte normal y la identidad
/// de la carga de trabajo; nunca sale de la configuración.
/// </summary>
internal sealed record KeyVaultTestClient(TokenCredential? Credential, HttpPipelineTransport Transport);

/// <summary>
/// Almacén de certificados del proveedor <c>KeyVault</c>: cada PKCS#12, con su contraseña (<see cref="CertificatePayload"/>),
/// es un secreto del vault llamado <c>impifact-cert-{empresa}-{blob}</c>, en base64 y con la empresa en una etiqueta. Key
/// Vault lo cifra con sus propias llaves. La referencia es <c>keyvault:{empresa}/{blob}</c>.
/// </summary>
/// <remarks>
/// Borrar inicia el borrado suave del vault: con la protección contra purga activada (Arquitectura detallada, sección
/// 19.3) el secreto se puede recuperar durante el plazo de retención. El certificado descifrado vive solo en memoria.
/// </remarks>
internal sealed class KeyVaultCertificateStore(KeyVaultClients clients) : ICertificateStore
{
    private const string Scheme = "keyvault";
    private const string ContentType = "application/x-impifact-certificate;v=1";

    private readonly Lazy<SecretClient> secrets = new(clients.Secrets);

    public async Task<string> SaveAsync(Guid companyId, byte[] pfx, string password, CancellationToken ct)
    {
        var blobId = Guid.CreateVersion7();
        var payload = CertificatePayload.Pack(pfx, password);
        try
        {
            var secret = new KeyVaultSecret(CertificateReference.SecretName(companyId, blobId), Convert.ToBase64String(payload));
            secret.Properties.ContentType = ContentType;
            secret.Properties.Tags["company"] = companyId.ToString("D");
            await secrets.Value.SetSecretAsync(secret, ct);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }

        return CertificateReference.Format(Scheme, companyId, blobId);
    }

    public async Task<X509Certificate2> LoadAsync(Guid companyId, string secretRef, CancellationToken ct)
    {
        var blobId = CertificateReference.Parse(Scheme, companyId, secretRef);
        KeyVaultSecret secret;
        try
        {
            secret = (await secrets.Value.GetSecretAsync(CertificateReference.SecretName(companyId, blobId), cancellationToken: ct)).Value;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            throw new FileNotFoundException("The certificate does not exist.");
        }

        var payload = Convert.FromBase64String(secret.Value);
        try
        {
            return CertificatePayload.Load(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public async Task DeleteAsync(Guid companyId, string secretRef, CancellationToken ct)
    {
        var blobId = CertificateReference.Parse(Scheme, companyId, secretRef);
        try
        {
            await secrets.Value.StartDeleteSecretAsync(CertificateReference.SecretName(companyId, blobId), ct);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            // Ya no existe (o ya está borrado): borrar dos veces no falla.
        }
    }
}

/// <summary>
/// Envuelve las llaves de Data Protection con la llave RSA <c>Secrets:KeyVault:KeyName</c> del vault (RSA-OAEP-256), en el
/// servicio: la llave privada nunca sale del vault. Al envolver se usa la versión vigente de la llave y queda anotada; al desenvolver se usa esa versión, siempre en el vault
/// configurado: del identificador anotado solo se toman el nombre, que debe ser el configurado, y la versión.
/// </summary>
internal sealed class KeyVaultKeyEncryptionKey(KeyVaultClients clients) : IKeyEncryptionKey
{
    private readonly ConcurrentDictionary<Uri, CryptographyClient> cryptography = new();

    public string Provider => SecretsOptions.KeyVaultProvider;

    // Data Protection crea una llave cada 90 días: leer la versión vigente en cada una sigue la rotación de la llave del
    // vault sin cachés que vencer.
    public (byte[] Wrapped, string KeyId) Wrap(byte[] key)
    {
        var current = clients.Keys().GetKey(clients.Settings.KeyName).Value;
        var result = Client(current.Id).WrapKey(KeyWrapAlgorithm.RsaOaep256, key);
        return (result.EncryptedKey, result.KeyId);
    }

    public byte[] Unwrap(string keyId, byte[] wrapped)
    {
        // Forma del identificador: https://<vault>/keys/<nombre>/<versión>.
        var segments = Uri.TryCreate(keyId, UriKind.Absolute, out var uri) ? uri.AbsolutePath.Trim('/').Split('/') : [];
        if (segments is not ["keys", var name, var version] || !string.Equals(name, clients.Settings.KeyName, StringComparison.OrdinalIgnoreCase))
        {
            throw new CryptographicException("The key was wrapped by a Key Vault key that is not the configured one.");
        }

        return Client(new Uri(clients.VaultUri, $"keys/{name}/{version}")).UnwrapKey(KeyWrapAlgorithm.RsaOaep256, wrapped).Key;
    }

    public async Task ProbeAsync(CancellationToken ct) => await clients.Keys().GetKeyAsync(clients.Settings.KeyName, cancellationToken: ct);

    private CryptographyClient Client(Uri keyId) => cryptography.GetOrAdd(keyId, clients.Cryptography);
}
