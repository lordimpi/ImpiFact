using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Google.Api.Gax.Grpc;
using Google.Api.Gax.ResourceNames;
using Google.Cloud.Kms.V1;
using Google.Cloud.SecretManager.V1;
using Google.Protobuf;
using Grpc.Core;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.Protection;

/// <summary>
/// Clientes de Google Secret Manager y de Cloud KMS del proveedor <c>GcpSecrets</c>, con las credenciales predeterminadas
/// de la aplicación (ADC: la cuenta de servicio de Cloud Run o de GKE). Con <c>Secrets:GcpSecrets:Endpoint</c> (solo un
/// emulador) hablan con esa dirección sin credenciales. Se crean la primera vez que se usan: así un equipo sin
/// credenciales arranca y lo informa <c>/health/ready</c>.
/// </summary>
internal sealed class GcpSecretsClients(IOptions<SecretsOptions> options)
{
    private readonly Lazy<SecretManagerServiceClient> secrets = new(() =>
    {
        var builder = new SecretManagerServiceClientBuilder();
        Configure(builder, options.Value.GcpSecrets);
        return builder.Build();
    });

    private readonly Lazy<KeyManagementServiceClient> kms = new(() =>
    {
        var builder = new KeyManagementServiceClientBuilder();
        Configure(builder, options.Value.GcpSecrets);
        return builder.Build();
    });

    public GcpSecretsOptions Settings => options.Value.GcpSecrets;

    public SecretManagerServiceClient Secrets => secrets.Value;

    public KeyManagementServiceClient Kms => kms.Value;

    private static void Configure<TClient>(ClientBuilderBase<TClient> builder, GcpSecretsOptions settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            var endpoint = new Uri(settings.Endpoint);
            builder.Endpoint = $"{endpoint.Host}:{endpoint.Port}";
            builder.ChannelCredentials = endpoint.Scheme == Uri.UriSchemeHttp ? ChannelCredentials.Insecure : ChannelCredentials.SecureSsl;
        }
    }
}

/// <summary>
/// Almacén de certificados del proveedor <c>GcpSecrets</c>: cada PKCS#12, con su contraseña (<see cref="CertificatePayload"/>),
/// es un secreto de Secret Manager llamado <c>impifact-cert-{empresa}-{blob}</c>, con replicación automática y la empresa
/// en una etiqueta, y su contenido es la primera versión. Secret Manager lo cifra con sus propias llaves. La referencia es
/// <c>gcp:{empresa}/{blob}</c>.
/// </summary>
/// <remarks>
/// Secret Manager no tiene papelera: borrar elimina el secreto y sus versiones. Por eso la copia fuera de línea del
/// certificado que guarda el propietario de la empresa (Arquitectura detallada, sección 19.3) es la recuperación.
/// </remarks>
internal sealed class GcpSecretManagerCertificateStore(GcpSecretsClients clients) : ICertificateStore
{
    private const string Scheme = "gcp";

    public async Task<string> SaveAsync(Guid companyId, byte[] pfx, string password, CancellationToken ct)
    {
        var blobId = Guid.CreateVersion7();
        var payload = CertificatePayload.Pack(pfx, password);
        try
        {
            var secret = await clients.Secrets.CreateSecretAsync(
                new ProjectName(clients.Settings.ProjectId),
                CertificateReference.SecretName(companyId, blobId),
                new Secret
                {
                    Replication = new Replication { Automatic = new Replication.Types.Automatic() },
                    Labels = { ["company"] = companyId.ToString("N") },
                },
                ct);
            await clients.Secrets.AddSecretVersionAsync(secret.SecretName, new SecretPayload { Data = ByteString.CopyFrom(payload) }, ct);
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
        AccessSecretVersionResponse response;
        try
        {
            response = await clients.Secrets.AccessSecretVersionAsync(
                new SecretVersionName(clients.Settings.ProjectId, CertificateReference.SecretName(companyId, blobId), "latest"), ct);
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.NotFound)
        {
            throw new FileNotFoundException("The certificate does not exist.");
        }

        var payload = response.Payload.Data.ToByteArray();
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
            await clients.Secrets.DeleteSecretAsync(new SecretName(clients.Settings.ProjectId, CertificateReference.SecretName(companyId, blobId)), ct);
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.NotFound)
        {
            // Ya no existe: borrar dos veces no falla.
        }
    }
}

/// <summary>
/// Envuelve las llaves de Data Protection con la llave simétrica de Cloud KMS <c>Secrets:GcpSecrets:KmsKeyName</c> (en el
/// servicio, con datos asociados fijos que Cloud KMS exige iguales al desenvolver). Al desenvolver se pide siempre la llave
/// configurada: Cloud KMS elige la versión que envolvió y rechaza un sobre de otra llave.
/// </summary>
internal sealed class GcpKmsKeyEncryptionKey(GcpSecretsClients clients) : IKeyEncryptionKey
{
    private static readonly ByteString AssociatedData = ByteString.CopyFromUtf8("impifact-data-protection-key");

    public string Provider => SecretsOptions.GcpSecretsProvider;

    public (byte[] Wrapped, string KeyId) Wrap(byte[] key)
    {
        var response = clients.Kms.Encrypt(new EncryptRequest
        {
            Name = clients.Settings.KmsKeyName,
            Plaintext = ByteString.CopyFrom(key),
            AdditionalAuthenticatedData = AssociatedData,
        });
        return (response.Ciphertext.ToByteArray(), string.IsNullOrEmpty(response.Name) ? clients.Settings.KmsKeyName : response.Name);
    }

    public byte[] Unwrap(string keyId, byte[] wrapped) =>
        clients.Kms.Decrypt(new DecryptRequest
        {
            Name = clients.Settings.KmsKeyName,
            Ciphertext = ByteString.CopyFrom(wrapped),
            AdditionalAuthenticatedData = AssociatedData,
        }).Plaintext.ToByteArray();

    public async Task ProbeAsync(CancellationToken ct) =>
        await clients.Kms.GetCryptoKeyAsync(CryptoKeyName.Parse(clients.Settings.KmsKeyName), ct);
}
