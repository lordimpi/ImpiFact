using System.Net;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Domain.Documents;
using ImpiFact.Infrastructure.Options;

namespace ImpiFact.Infrastructure.Storage;

/// <summary>
/// Almacenamiento de archivos en un bucket de S3 (Amazon Simple Storage Service, el almacenamiento de objetos de
/// AWS) o de un servicio compatible como MinIO (supuesto C7 de la fase 4). Cada clave válida (<see cref="BlobKey"/>,
/// la misma regla que el local) es la clave del objeto dentro del bucket.
/// </summary>
/// <remarks>
/// En S3 guardar un objeto es atómico: quien lee ve el anterior o el nuevo completo. Por omisión cada objeto se
/// guarda con SSE-S3 (cifrado del lado del servidor con llaves que administra el servicio, AES-256). Las URL
/// prefirmadas (enlaces temporales que dan acceso de lectura a un solo objeto sin otra credencial) se firman en este
/// proceso, sin llamar al servicio, y vencen como máximo en <see cref="S3StorageOptions.SignedUrlMaxMinutes"/>.
/// </remarks>
internal sealed class S3BlobStorage : IBlobStorage, IDisposable
{
    private readonly IAmazonS3 client;
    private readonly S3StorageOptions options;
    private readonly Protocol signedUrlProtocol;

    public S3BlobStorage(IAmazonS3 client, S3StorageOptions options)
    {
        this.client = client;
        this.options = options;
        // La firma lleva el esquema de la URL: con un servicio compatible por http (MinIO local) la URL debe ser http.
        signedUrlProtocol = Uri.TryCreate(options.ServiceUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttp
            ? Protocol.HTTP
            : Protocol.HTTPS;
    }

    public StorageProvider Provider => StorageProvider.S3;

    /// <summary>
    /// Crea el almacén con su propio cliente. Sin <paramref name="credentials"/> el cliente usa la cadena estándar
    /// de credenciales de AWS (variables de entorno, perfil, rol de la instancia o de la tarea).
    /// </summary>
    public static S3BlobStorage Create(S3StorageOptions options, AWSCredentials? credentials)
    {
        var config = new AmazonS3Config { ForcePathStyle = options.ForcePathStyle };
        if (string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }
        else
        {
            config.ServiceURL = options.ServiceUrl;
            config.AuthenticationRegion = string.IsNullOrWhiteSpace(options.Region) ? "us-east-1" : options.Region;
            // Las sumas de verificación adicionales que el SDK agrega por omisión no las entienden todos los servicios
            // compatibles; con ellos solo se usan cuando la operación las exige.
            config.RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED;
            config.ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED;
        }

        var client = credentials is null ? new AmazonS3Client(config) : new AmazonS3Client(credentials, config);
        return new S3BlobStorage(client, options);
    }

    public async Task<string> PutAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        BlobKey.Validate(key);

        // El SDK necesita conocer el largo para firmar la solicitud; un flujo que no se puede rebobinar se copia antes.
        var buffered = content.CanSeek ? null : new MemoryStream();
        try
        {
            if (buffered is not null)
            {
                await content.CopyToAsync(buffered, ct);
                buffered.Position = 0;
            }

            var request = new PutObjectRequest
            {
                BucketName = options.Bucket,
                Key = key,
                InputStream = buffered ?? content,
                ContentType = contentType,
                AutoCloseStream = false,
            };
            if (options.ServerSideEncryption)
            {
                request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;
            }

            await client.PutObjectAsync(request, ct);
            return key;
        }
        finally
        {
            buffered?.Dispose();
        }
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct)
    {
        BlobKey.Validate(key);
        try
        {
            // Liberar el flujo de la respuesta cierra también la conexión HTTP que lo trae.
            var response = await client.GetObjectAsync(options.Bucket, key, ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound && exception.ErrorCode == "NoSuchKey")
        {
            throw new BlobNotFoundException(key);
        }
    }

    // S3 responde con éxito al borrar un objeto que no existe, así que borrar dos veces no falla.
    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        BlobKey.Validate(key);
        await client.DeleteObjectAsync(options.Bucket, key, ct);
    }

    public Uri CreateSignedUrl(string key, TimeSpan lifetime)
    {
        BlobKey.Validate(key);
        var limit = TimeSpan.FromMinutes(options.SignedUrlMaxMinutes);
        if (lifetime <= TimeSpan.Zero || lifetime > limit)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, $"A signed URL must last more than zero and at most {limit}.");
        }

        // La firma fecha la URL en segundos enteros (hacia abajo) y redondea la vigencia hacia arriba: si el vencimiento
        // se calculara desde la hora con fracción, el enlace duraría un segundo más de lo pedido y, en el límite de
        // 7 días, AWS lo rechazaría. Por eso se parte del segundo entero actual.
        var now = DateTime.UtcNow;
        var url = client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = options.Bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Protocol = signedUrlProtocol,
            Expires = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond)).Add(lifetime),
        });
        return new Uri(url);
    }

    public void Dispose() => client.Dispose();
}

/// <summary>
/// Credenciales fijas para el cliente de S3, solo para pruebas (por ejemplo, las de un MinIO desechable). Si no se
/// registran, el cliente usa la cadena estándar de credenciales de AWS; nunca salen de la configuración.
/// </summary>
internal sealed record S3Credentials(AWSCredentials Value);
