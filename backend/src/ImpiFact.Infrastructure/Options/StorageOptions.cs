using System.ComponentModel.DataAnnotations;

namespace ImpiFact.Infrastructure.Options;

/// <summary>
/// Almacenamiento de archivos (sección «Storage»; Arquitectura detallada, sección 11). En la fase 3 solo existe el
/// proveedor <c>Local</c> (supuesto B3: el XML firmado se guarda al emitir); los de nube (<c>AzureBlob</c>,
/// <c>S3</c>, <c>Gcs</c>) llegan en la fase 4 y elegirlos hoy detiene el arranque.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public const string LocalProvider = "Local";

    [Required]
    [AllowedValues(LocalProvider, "AzureBlob", "S3", "Gcs")]
    public string Provider { get; set; } = "";

    /// <summary>Ajustes del proveedor <c>Local</c>; solo se exigen cuando ese es el proveedor.</summary>
    public LocalStorageOptions Local { get; set; } = new();
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
