using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Domain.Documents;
using ImpiFact.Infrastructure.Options;

namespace ImpiFact.Infrastructure.Storage;

/// <summary>
/// Almacenamiento de archivos en un contenedor de Azure Blob Storage (o de Azurite, su emulador). Cada clave válida
/// (<see cref="BlobKey"/>, la misma regla que el local y S3) es el nombre del blob dentro del contenedor, con el mismo
/// tipo de contenido que se guardó.
/// </summary>
/// <remarks>
/// <para>
/// En Azure guardar un blob es atómico: quien lee ve el anterior o el nuevo completo. El servicio cifra siempre los
/// datos en reposo (AES-256 con llaves de Microsoft o del cliente, según la cuenta), así que aquí no hay opción de
/// cifrado. El contenedor debe existir: un contenedor que falta no cuenta como «el archivo no existe» y hace fallar
/// <c>/health/ready</c>.
/// </para>
/// <para>
/// Las URL firmadas son SAS de solo lectura de un blob (firmas de acceso compartido: un enlace temporal que da acceso a
/// un solo archivo sin otra credencial). Con la identidad de la carga de trabajo se firman con una llave de delegación
/// de usuario, que se pide al servicio y se reutiliza mientras cubra la vigencia pedida; con una llave de cuenta (solo
/// Azurite en las pruebas) se firman en este proceso.
/// </para>
/// </remarks>
internal sealed class AzureBlobStorage : IBlobStorage
{
    /// <summary>Margen hacia atrás del inicio de la llave de delegación, por diferencias de reloj con el servicio.</summary>
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    private readonly BlobServiceClient service;
    private readonly BlobContainerClient container;
    private readonly AzureBlobStorageOptions options;
    private readonly SasProtocol protocol;
    private readonly Lock delegationLock = new();
    private UserDelegationKey? delegationKey;

    public AzureBlobStorage(BlobServiceClient service, AzureBlobStorageOptions options)
    {
        this.service = service;
        this.options = options;
        container = service.GetBlobContainerClient(options.Container);
        // Azurite atiende por http; en Azure la firma exige https.
        protocol = service.Uri.Scheme == Uri.UriSchemeHttp ? SasProtocol.HttpsAndHttp : SasProtocol.Https;
    }

    public StorageProvider Provider => StorageProvider.AzureBlob;

    /// <summary>
    /// Crea el almacén. Sin <paramref name="client"/> (que solo pasan las pruebas, con la cuenta de Azurite) el cliente
    /// usa la identidad de la carga de trabajo con <see cref="DefaultAzureCredential"/>, sin llaves; con
    /// <see cref="AzureBlobStorageOptions.UseEmulatorAccount"/>, la cuenta de desarrollo de Azurite que trae el SDK.
    /// </summary>
    public static AzureBlobStorage Create(AzureBlobStorageOptions options, AzureBlobServiceClient? client)
    {
        if (client is not null)
        {
            return new AzureBlobStorage(client.Value, options);
        }

        var serviceUri = new Uri(options.ServiceUri);
        if (options.UseEmulatorAccount)
        {
            // El SDK arma la cuenta pública de Azurite (devstoreaccount1, puerto 10000) en el host indicado; aquí no hay
            // ninguna llave.
            var proxy = serviceUri.GetLeftPart(UriPartial.Scheme) + serviceUri.Host;
            return new AzureBlobStorage(new BlobServiceClient($"UseDevelopmentStorage=true;DevelopmentStorageProxyUri={proxy}"), options);
        }

        return new AzureBlobStorage(new BlobServiceClient(serviceUri, new DefaultAzureCredential()), options);
    }

    /// <summary>Si la dirección es la de la cuenta de desarrollo de Azurite: <c>http://&lt;host&gt;:10000/devstoreaccount1</c>.</summary>
    internal static bool IsEmulatorEndpoint(Uri serviceUri) =>
        serviceUri.Scheme == Uri.UriSchemeHttp
        && serviceUri.Port == 10000
        && serviceUri.AbsolutePath.TrimEnd('/') == "/devstoreaccount1";

    public async Task<string> PutAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        BlobKey.Validate(key);

        await container.GetBlobClient(key).UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            ct);
        return key;
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct)
    {
        BlobKey.Validate(key);
        try
        {
            // Liberar el flujo de la respuesta cierra también la conexión HTTP que lo trae.
            var response = await container.GetBlobClient(key).DownloadStreamingAsync(cancellationToken: ct);
            return response.Value.Content;
        }
        catch (RequestFailedException exception) when (exception.ErrorCode == BlobErrorCode.BlobNotFound)
        {
            throw new BlobNotFoundException(key);
        }
    }

    // Borrar un blob que no existe no falla, igual que en S3; un contenedor que no existe sí.
    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        BlobKey.Validate(key);
        try
        {
            await container.GetBlobClient(key).DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: ct);
        }
        catch (RequestFailedException exception) when (exception.ErrorCode == BlobErrorCode.BlobNotFound)
        {
        }
    }

    public Uri CreateSignedUrl(string key, TimeSpan lifetime)
    {
        BlobKey.Validate(key);
        var limit = TimeSpan.FromMinutes(options.SignedUrlMaxMinutes);
        if (lifetime <= TimeSpan.Zero || lifetime > limit)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, $"A signed URL must last more than zero and at most {limit}.");
        }

        var blob = container.GetBlobClient(key);
        var expiresOn = DateTimeOffset.UtcNow.Add(lifetime);
        var sas = new BlobSasBuilder(BlobSasPermissions.Read, expiresOn)
        {
            BlobContainerName = container.Name,
            BlobName = key,
            Resource = "b",
            Protocol = protocol,
        };
        if (blob.CanGenerateSasUri)
        {
            return blob.GenerateSasUri(sas);
        }

        var signed = new BlobUriBuilder(blob.Uri) { Sas = sas.ToSasQueryParameters(DelegationKey(expiresOn), service.AccountName) };
        return signed.ToUri();
    }

    /// <summary>
    /// Llave de delegación de usuario que cubre hasta <paramref name="expiresOn"/>. Se pide por 7 días (lo máximo) y se
    /// reutiliza mientras alcance, así casi ninguna firma llama al servicio.
    /// </summary>
    private UserDelegationKey DelegationKey(DateTimeOffset expiresOn)
    {
        lock (delegationLock)
        {
            if (delegationKey is null || delegationKey.SignedExpiresOn < expiresOn)
            {
                var now = DateTimeOffset.UtcNow;
                delegationKey = service.GetUserDelegationKey(
                    new BlobGetUserDelegationKeyOptions(now.AddMinutes(AzureBlobStorageOptions.SignedUrlLimitMinutes)) { StartsOn = now - ClockSkew }).Value;
            }

            return delegationKey;
        }
    }
}

/// <summary>
/// Cliente de la cuenta de blobs ya armado, solo para pruebas (la cuenta de desarrollo de Azurite). Si no se registra, el
/// almacén usa la identidad de la carga de trabajo; nunca sale de la configuración.
/// </summary>
internal sealed record AzureBlobServiceClient(BlobServiceClient Value);
