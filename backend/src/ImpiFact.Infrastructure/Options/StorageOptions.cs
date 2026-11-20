using System.ComponentModel.DataAnnotations;

namespace ImpiFact.Infrastructure.Options;

/// <summary>
/// Almacenamiento de archivos (sección «Storage»; Arquitectura detallada, sección 11). Existen los proveedores
/// <c>Local</c> (supuesto B3 de la fase 3: el XML firmado se guarda al emitir), <c>S3</c> (fase 4, supuesto C7) y
/// <c>AzureBlob</c> (fase 6); <c>Gcs</c> llega en la fase 6 y elegirlo hoy detiene el arranque.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public const string LocalProvider = "Local";

    public const string S3Provider = "S3";

    public const string AzureBlobProvider = "AzureBlob";

    [Required]
    [AllowedValues(LocalProvider, AzureBlobProvider, S3Provider, "Gcs")]
    public string Provider { get; set; } = "";

    /// <summary>Ajustes del proveedor <c>Local</c>; solo se exigen cuando ese es el proveedor.</summary>
    public LocalStorageOptions Local { get; set; } = new();

    /// <summary>Ajustes del proveedor <c>S3</c>; solo se exigen cuando ese es el proveedor.</summary>
    public S3StorageOptions S3 { get; set; } = new();

    /// <summary>Ajustes del proveedor <c>AzureBlob</c>; solo se exigen cuando ese es el proveedor.</summary>
    public AzureBlobStorageOptions AzureBlob { get; set; } = new();
}

/// <summary>Proveedor local: los archivos van a una carpeta del servidor, por ejemplo un volumen de Docker.</summary>
public sealed class LocalStorageOptions
{
    /// <summary>
    /// Carpeta raíz, con ruta absoluta (por ejemplo <c>/data/files</c>). Llega por la configuración del despliegue
    /// (<c>Storage__Local__Root</c>); cada clave de archivo es una ruta relativa dentro de esta carpeta.
    /// </summary>
    public string Root { get; set; } = "";
}

/// <summary>
/// Proveedor S3 (Amazon Simple Storage Service, el almacenamiento de objetos de AWS) o un servicio compatible, como
/// MinIO en desarrollo. Cada clave de archivo es la clave del objeto dentro del bucket (el contenedor de objetos).
/// </summary>
/// <remarks>
/// Aquí no hay llaves de acceso: las credenciales salen de la cadena estándar de AWS (variables de entorno
/// <c>AWS_ACCESS_KEY_ID</c> y <c>AWS_SECRET_ACCESS_KEY</c>, perfil de <c>~/.aws</c>, o el rol de la instancia o de
/// la tarea en la nube), así nunca quedan en <c>appsettings</c>.
/// </remarks>
public sealed class S3StorageOptions
{
    /// <summary>Límite superior de <see cref="SignedUrlMaxMinutes"/>: 7 días, lo máximo que admite una firma de AWS.</summary>
    public const int SignedUrlLimitMinutes = 7 * 24 * 60;

    /// <summary>Bucket donde se guardan los archivos; debe existir (lo crea el despliegue, no la aplicación).</summary>
    public string Bucket { get; set; } = "";

    /// <summary>
    /// Región de AWS del bucket, por ejemplo <c>us-east-1</c>. Se exige salvo que haya <see cref="ServiceUrl"/>;
    /// en ese caso, si falta, se firma con <c>us-east-1</c>.
    /// </summary>
    public string Region { get; set; } = "";

    /// <summary>
    /// URL de un servicio compatible con S3 (por ejemplo <c>http://127.0.0.1:9000</c> para MinIO). Vacía para usar
    /// AWS. Debe ser absoluta, <c>http</c> o <c>https</c>, sin usuario, contraseña ni consulta.
    /// </summary>
    public string ServiceUrl { get; set; } = "";

    /// <summary>
    /// Pone el bucket en la ruta de la URL (<c>host/bucket/clave</c>) en vez de en el nombre del host; lo piden
    /// MinIO y la mayoría de los servicios compatibles.
    /// </summary>
    public bool ForcePathStyle { get; set; }

    /// <summary>
    /// Pide al servicio que cifre cada objeto al guardarlo (SSE-S3: cifrado del lado del servidor con llaves que
    /// administra el servicio, AES-256). Activo por omisión; se apaga solo si el bucket ya aplica otro cifrado.
    /// </summary>
    public bool ServerSideEncryption { get; set; } = true;

    /// <summary>Vigencia máxima, en minutos, de una URL prefirmada de descarga (por omisión una hora).</summary>
    public int SignedUrlMaxMinutes { get; set; } = 60;
}

/// <summary>
/// Proveedor <c>AzureBlob</c>: Azure Blob Storage, el almacenamiento de objetos de Azure (Azurite en desarrollo, perfil
/// <c>azurite</c> de <c>deploy/docker-compose.yml</c>). Cada clave de archivo es el nombre del blob dentro del
/// contenedor (el equivalente del bucket de S3).
/// </summary>
/// <remarks>
/// Aquí no hay llaves ni SAS: el cliente usa la identidad de la carga de trabajo con <c>DefaultAzureCredential</c>
/// (identidad administrada en Azure; variables <c>AZURE_CLIENT_ID</c>, <c>AZURE_TENANT_ID</c> y el token federado
/// en Kubernetes; la sesión de la CLI de Azure en un equipo de desarrollo). La identidad necesita el rol
/// «Storage Blob Data Contributor» sobre el contenedor y, para las URL firmadas, «Storage Blob Delegator» sobre la
/// cuenta.
/// </remarks>
public sealed class AzureBlobStorageOptions
{
    /// <summary>Límite superior de <see cref="SignedUrlMaxMinutes"/>: 7 días, lo máximo de una llave de delegación.</summary>
    public const int SignedUrlLimitMinutes = 7 * 24 * 60;

    /// <summary>
    /// Dirección del servicio de blobs de la cuenta, por ejemplo <c>https://impifactdocs.blob.core.windows.net</c>.
    /// Con Azurite es <c>http://127.0.0.1:10000/devstoreaccount1</c>. Absoluta, <c>http</c> o <c>https</c>, sin
    /// usuario, contraseña, consulta (una SAS) ni fragmento.
    /// </summary>
    public string ServiceUri { get; set; } = "";

    /// <summary>Contenedor donde se guardan los archivos; debe existir (lo crea el despliegue, no la aplicación).</summary>
    public string Container { get; set; } = "";

    /// <summary>Vigencia máxima, en minutos, de una URL firmada (SAS de solo lectura) de descarga; por omisión una hora.</summary>
    public int SignedUrlMaxMinutes { get; set; } = 60;

    /// <summary>
    /// Solo para Azurite (perfil <c>azurite</c> del compose): entra con la cuenta de desarrollo que el SDK de Azure ya
    /// conoce (<c>UseDevelopmentStorage=true</c>, la cuenta pública <c>devstoreaccount1</c>), porque Azurite no acepta
    /// la identidad de la carga de trabajo por http. Exige <see cref="ServiceUri"/> de la forma
    /// <c>http://&lt;host&gt;:10000/devstoreaccount1</c>; con una cuenta de Azure no sirve. Apagado por omisión.
    /// </summary>
    public bool UseEmulatorAccount { get; set; }
}
