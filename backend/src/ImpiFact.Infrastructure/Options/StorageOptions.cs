using System.ComponentModel.DataAnnotations;

namespace ImpiFact.Infrastructure.Options;

/// <summary>
/// Almacenamiento de archivos (sección «Storage»; Arquitectura detallada, sección 11). Existen los proveedores
/// <c>Local</c> (supuesto B3 de la fase 3: el XML firmado se guarda al emitir) y <c>S3</c> (fase 4, supuesto C7);
/// los demás de nube (<c>AzureBlob</c>, <c>Gcs</c>) llegan en la fase 6 y elegirlos hoy detiene el arranque.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public const string LocalProvider = "Local";

    public const string S3Provider = "S3";

    [Required]
    [AllowedValues(LocalProvider, "AzureBlob", S3Provider, "Gcs")]
    public string Provider { get; set; } = "";

    /// <summary>Ajustes del proveedor <c>Local</c>; solo se exigen cuando ese es el proveedor.</summary>
    public LocalStorageOptions Local { get; set; } = new();

    /// <summary>Ajustes del proveedor <c>S3</c>; solo se exigen cuando ese es el proveedor.</summary>
    public S3StorageOptions S3 { get; set; } = new();
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
