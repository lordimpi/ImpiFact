using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Abstractions.Storage;

/// <summary>
/// Almacenamiento de archivos (Arquitectura detallada, sección 5): XML, PDF, solicitudes y respuestas de la DIAN.
/// Las filas de la base guardan solo la clave del archivo, nunca su contenido (Modelo de datos detallado,
/// principio 4).
/// </summary>
/// <remarks>
/// Una clave es una ruta relativa con tramos separados por «/»; cada tramo empieza con una letra o un dígito y solo
/// lleva letras, dígitos, «.», «_» o «-». Una clave con otra forma (vacía, absoluta, con «..» o con «\») se rechaza
/// con <see cref="ArgumentException"/>, para que nadie lea ni escriba fuera del almacén.
/// </remarks>
public interface IBlobStorage
{
    /// <summary>Proveedor de este almacén; se guarda en <c>document_files.storage_provider</c>.</summary>
    StorageProvider Provider { get; }

    /// <summary>
    /// Guarda el contenido bajo la clave, reemplazando el anterior si ya existía. La escritura es atómica: quien lee
    /// ve el archivo anterior o el nuevo completo, nunca uno a medias.
    /// </summary>
    /// <returns>La clave con la que quedó guardado.</returns>
    Task<string> PutAsync(string key, Stream content, string contentType, CancellationToken ct);

    /// <summary>Abre el archivo para leerlo; quien lo pide libera el flujo.</summary>
    /// <exception cref="BlobNotFoundException">Si no hay archivo con esa clave.</exception>
    Task<Stream> OpenReadAsync(string key, CancellationToken ct);

    /// <summary>Borra el archivo; no hace nada si ya no existe.</summary>
    Task DeleteAsync(string key, CancellationToken ct);

    /// <summary>URL firmada (enlace temporal que da acceso sin otra credencial) para descargar el archivo.</summary>
    /// <exception cref="NotSupportedException">Si el proveedor no puede firmar URL (el local).</exception>
    Uri CreateSignedUrl(string key, TimeSpan lifetime);
}

/// <summary>No hay archivo con la clave pedida.</summary>
public sealed class BlobNotFoundException(string key) : Exception($"No file is stored under key '{key}'.")
{
    public string Key { get; } = key;
}
