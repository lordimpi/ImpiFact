using System.Net;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Domain.Documents;
using ImpiFact.Infrastructure.Options;

namespace ImpiFact.Infrastructure.Storage;

/// <summary>
/// Almacenamiento de archivos en un bucket de Google Cloud Storage (o de fake-gcs-server, su emulador). Cada clave válida
/// (<see cref="BlobKey"/>, la misma regla que el local, S3 y Azure) es el nombre del objeto dentro del bucket, con el
/// mismo tipo de contenido que se guardó.
/// </summary>
/// <remarks>
/// <para>
/// En Cloud Storage guardar un objeto es atómico y el servicio cifra siempre los datos en reposo. El bucket debe
/// existir: como el servicio contesta «no existe» (404) igual para un objeto que para el bucket, ante un 404 se revisa
/// el bucket, y si falta no cuenta como «el archivo no existe» y hace fallar <c>/health/ready</c>.
/// </para>
/// <para>
/// La lectura baja el objeto completo a memoria antes de devolverlo (el SDK no entrega un flujo de lectura directo); los
/// archivos de un documento (XML, PDF, ZIP) son de pocos cientos de kB. Las URL firmadas son V4 de solo lectura y las
/// firma la cuenta de servicio de las credenciales predeterminadas (en Cloud Run o GKE, con la API de IAM).
/// </para>
/// </remarks>
internal sealed class GcsBlobStorage : IBlobStorage, IDisposable
{
    private readonly Lazy<StorageClient> client;
    private readonly GcsStorageOptions options;
    private readonly Lazy<UrlSigner> signer;

    /// <param name="client">
    /// Arma el cliente la primera vez que se usa: las credenciales predeterminadas se buscan entonces, no al registrar el
    /// servicio, así un equipo sin ellas arranca y lo informa <c>/health/ready</c>.
    /// </param>
    public GcsBlobStorage(Func<StorageClient> client, GcsStorageOptions options, Func<UrlSigner> signer)
    {
        this.client = new Lazy<StorageClient>(client, LazyThreadSafetyMode.ExecutionAndPublication);
        this.options = options;
        this.signer = new Lazy<UrlSigner>(signer, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public StorageProvider Provider => StorageProvider.Gcs;

    /// <summary>
    /// Crea el almacén. Contra Google Cloud usa las credenciales predeterminadas de la aplicación (ADC), sin llaves; con
    /// <see cref="GcsStorageOptions.Endpoint"/> (un emulador) no manda credenciales. El firmante de URL sale de
    /// <paramref name="signer"/> (solo pruebas) o de las mismas credenciales, la primera vez que se firma.
    /// </summary>
    public static GcsBlobStorage Create(GcsStorageOptions options, GcsUrlSigner? signer)
    {
        var builder = new StorageClientBuilder();
        if (!string.IsNullOrWhiteSpace(options.Endpoint))
        {
            builder.BaseUri = options.Endpoint.TrimEnd('/') + "/storage/v1/";
            builder.UnauthenticatedAccess = true;
        }

        return new GcsBlobStorage(
            builder.Build,
            options,
            () => signer?.Value ?? UrlSigner.FromCredential(GoogleCredential.GetApplicationDefault()));
    }

    public async Task<string> PutAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        BlobKey.Validate(key);

        await client.Value.UploadObjectAsync(options.Bucket, key, contentType, content, cancellationToken: ct);
        return key;
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct)
    {
        BlobKey.Validate(key);
        var buffer = new MemoryStream();
        try
        {
            await client.Value.DownloadObjectAsync(options.Bucket, key, buffer, cancellationToken: ct);
            buffer.Position = 0;
            return buffer;
        }
        catch (GoogleApiException exception) when (exception.HttpStatusCode == HttpStatusCode.NotFound)
        {
            await buffer.DisposeAsync();
            await EnsureBucketExistsAsync(exception, ct);
            throw new BlobNotFoundException(key);
        }
        catch
        {
            await buffer.DisposeAsync();
            throw;
        }
    }

    // Borrar un objeto que no existe no falla, igual que en S3 y Azure; un bucket que no existe sí.
    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        BlobKey.Validate(key);
        try
        {
            await client.Value.DeleteObjectAsync(options.Bucket, key, cancellationToken: ct);
        }
        catch (GoogleApiException exception) when (exception.HttpStatusCode == HttpStatusCode.NotFound)
        {
            await EnsureBucketExistsAsync(exception, ct);
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

        var url = signer.Value.Sign(
            UrlSigner.RequestTemplate.FromBucket(options.Bucket).WithObjectName(key).WithHttpMethod(HttpMethod.Get),
            UrlSigner.Options.FromDuration(lifetime).WithSigningVersion(SigningVersion.V4));
        return new Uri(url);
    }

    public void Dispose()
    {
        if (client.IsValueCreated)
        {
            client.Value.Dispose();
        }
    }

    /// <summary>Ante un 404, distingue un objeto que falta de un bucket que falta: este último se informa tal cual.</summary>
    private async Task EnsureBucketExistsAsync(GoogleApiException notFound, CancellationToken ct)
    {
        try
        {
            await client.Value.GetBucketAsync(options.Bucket, cancellationToken: ct);
        }
        catch (GoogleApiException exception) when (exception.HttpStatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"The Cloud Storage bucket '{options.Bucket}' does not exist.", notFound);
        }
    }
}

/// <summary>
/// Firmante de URL fijo, solo para pruebas (con una cuenta de servicio generada en la prueba). Si no se registra, se
/// firma con las credenciales predeterminadas de la aplicación; nunca sale de la configuración.
/// </summary>
internal sealed record GcsUrlSigner(UrlSigner Value);
