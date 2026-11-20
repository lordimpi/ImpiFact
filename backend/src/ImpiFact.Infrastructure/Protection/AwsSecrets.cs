using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Amazon;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Amazon.Runtime;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.Options;
using SecretsTag = Amazon.SecretsManager.Model.Tag;

namespace ImpiFact.Infrastructure.Protection;

/// <summary>
/// Clientes de AWS Secrets Manager y de AWS KMS del proveedor <c>AwsSecrets</c>, con la cadena estándar de credenciales de
/// AWS (el rol de la tarea o de la cuenta de servicio en la nube), o las credenciales de <see cref="AwsSecretsCredentials"/>
/// en las pruebas contra Moto. Se crean la primera vez que se usan.
/// </summary>
internal sealed class AwsSecretsClients(IOptions<SecretsOptions> options, AwsSecretsCredentials? test = null) : IDisposable
{
    private readonly Lazy<IAmazonSecretsManager> secrets = new(() =>
        Create(options.Value.AwsSecrets, test, (credentials, config) => credentials is null
            ? new AmazonSecretsManagerClient(config)
            : new AmazonSecretsManagerClient(credentials, config), new AmazonSecretsManagerConfig()));

    private readonly Lazy<IAmazonKeyManagementService> kms = new(() =>
        Create(options.Value.AwsSecrets, test, (credentials, config) => credentials is null
            ? new AmazonKeyManagementServiceClient(config)
            : new AmazonKeyManagementServiceClient(credentials, config), new AmazonKeyManagementServiceConfig()));

    public AwsSecretsOptions Settings => options.Value.AwsSecrets;

    public IAmazonSecretsManager Secrets => secrets.Value;

    public IAmazonKeyManagementService Kms => kms.Value;

    public void Dispose()
    {
        if (secrets.IsValueCreated)
        {
            secrets.Value.Dispose();
        }

        if (kms.IsValueCreated)
        {
            kms.Value.Dispose();
        }
    }

    private static TClient Create<TClient, TConfig>(
        AwsSecretsOptions settings, AwsSecretsCredentials? test, Func<AWSCredentials?, TConfig, TClient> factory, TConfig config)
        where TConfig : ClientConfig
    {
        if (string.IsNullOrWhiteSpace(settings.ServiceUrl))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(settings.Region);
        }
        else
        {
            config.ServiceURL = settings.ServiceUrl;
            config.AuthenticationRegion = string.IsNullOrWhiteSpace(settings.Region) ? "us-east-1" : settings.Region;
        }

        return factory(test?.Value, config);
    }
}

/// <summary>
/// Credenciales fijas para Secrets Manager y KMS, solo para pruebas (las de un Moto desechable). Si no se registran, los
/// clientes usan la cadena estándar de credenciales de AWS; nunca salen de la configuración.
/// </summary>
internal sealed record AwsSecretsCredentials(AWSCredentials Value);

/// <summary>
/// Almacén de certificados del proveedor <c>AwsSecrets</c>: cada PKCS#12, con su contraseña (<see cref="CertificatePayload"/>),
/// es un secreto binario de Secrets Manager llamado <c>impifact/certificates/{empresa}/{blob}</c>, cifrado con la llave de
/// KMS configurada y con la empresa en una etiqueta. La referencia es <c>aws:{empresa}/{blob}</c>.
/// </summary>
/// <remarks>
/// Borrar programa el borrado con la ventana de recuperación de Secrets Manager (30 días): durante ese plazo el secreto se
/// puede restaurar y no se puede leer, así que cuenta como «no existe».
/// </remarks>
internal sealed class AwsSecretsManagerCertificateStore(AwsSecretsClients clients) : ICertificateStore
{
    private const string Scheme = "aws";
    private const int RecoveryWindowDays = 30;

    public async Task<string> SaveAsync(Guid companyId, byte[] pfx, string password, CancellationToken ct)
    {
        var blobId = Guid.CreateVersion7();
        var payload = CertificatePayload.Pack(pfx, password);
        try
        {
            using var binary = new MemoryStream(payload, writable: false);
            await clients.Secrets.CreateSecretAsync(
                new CreateSecretRequest
                {
                    Name = SecretName(companyId, blobId),
                    SecretBinary = binary,
                    KmsKeyId = clients.Settings.KmsKeyId,
                    Description = "ImpiFact company certificate (PKCS#12 and its password).",
                    Tags = [new SecretsTag { Key = "company", Value = companyId.ToString("D") }],
                },
                ct);
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
        GetSecretValueResponse response;
        try
        {
            response = await clients.Secrets.GetSecretValueAsync(new GetSecretValueRequest { SecretId = SecretName(companyId, blobId) }, ct);
        }
        catch (Exception exception) when (IsMissing(exception))
        {
            throw new FileNotFoundException("The certificate does not exist.");
        }

        var payload = response.SecretBinary?.ToArray() ?? throw new CryptographicException("The stored certificate has an unknown format.");
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
            await clients.Secrets.DeleteSecretAsync(
                new DeleteSecretRequest { SecretId = SecretName(companyId, blobId), RecoveryWindowInDays = RecoveryWindowDays }, ct);
        }
        catch (Exception exception) when (IsMissing(exception))
        {
            // Ya no existe o ya está programado para borrarse: borrar dos veces no falla.
        }
    }

    private static string SecretName(Guid companyId, Guid blobId) => $"impifact/certificates/{companyId:D}/{blobId:D}";

    // Secrets Manager contesta «no existe» para un secreto que nunca existió y «solicitud inválida» para uno marcado para
    // borrarse («marked for deletion» en AWS, «marked deleted» en Moto); los dos son, para quien llama, un certificado que
    // ya no está.
    private static bool IsMissing(Exception exception) =>
        exception is Amazon.SecretsManager.Model.ResourceNotFoundException
        || (exception is InvalidRequestException invalid && invalid.Message.Contains("marked", StringComparison.OrdinalIgnoreCase)
            && invalid.Message.Contains("delet", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Envuelve las llaves de Data Protection con la llave de KMS <c>Secrets:AwsSecrets:KmsKeyId</c> (cifrado simétrico en el
/// servicio, con un contexto de cifrado fijo que KMS exige igual al desenvolver). Al desenvolver se pide siempre la llave
/// configurada: KMS rechaza un sobre que envolvió otra llave.
/// </summary>
/// <remarks>Data Protection pide cifrar y descifrar de forma sincrónica; el SDK de AWS solo trae llamadas asíncronas.</remarks>
internal sealed class AwsKmsKeyEncryptionKey(AwsSecretsClients clients) : IKeyEncryptionKey
{
    private static readonly Dictionary<string, string> Context = new() { ["purpose"] = "impifact-data-protection-key" };

    public string Provider => SecretsOptions.AwsSecretsProvider;

    public (byte[] Wrapped, string KeyId) Wrap(byte[] key)
    {
        using var plaintext = new MemoryStream(key, writable: false);
        var response = clients.Kms.EncryptAsync(new EncryptRequest
        {
            KeyId = clients.Settings.KmsKeyId,
            Plaintext = plaintext,
            EncryptionContext = new Dictionary<string, string>(Context),
        }).GetAwaiter().GetResult();
        return (response.CiphertextBlob.ToArray(), response.KeyId);
    }

    public byte[] Unwrap(string keyId, byte[] wrapped)
    {
        using var ciphertext = new MemoryStream(wrapped, writable: false);
        var response = clients.Kms.DecryptAsync(new DecryptRequest
        {
            KeyId = clients.Settings.KmsKeyId,
            CiphertextBlob = ciphertext,
            EncryptionContext = new Dictionary<string, string>(Context),
        }).GetAwaiter().GetResult();
        return response.Plaintext.ToArray();
    }

    public async Task ProbeAsync(CancellationToken ct)
    {
        var response = await clients.Kms.DescribeKeyAsync(new DescribeKeyRequest { KeyId = clients.Settings.KmsKeyId }, ct);
        if (response.KeyMetadata.Enabled != true)
        {
            throw new InvalidOperationException("The KMS key is disabled.");
        }
    }
}
