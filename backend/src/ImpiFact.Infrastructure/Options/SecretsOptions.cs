using System.ComponentModel.DataAnnotations;

namespace ImpiFact.Infrastructure.Options;

/// <summary>
/// Dónde se guardan los certificados y las claves de las empresas (Arquitectura detallada, sección 11).
/// <c>LocalAesGcm</c> cifra localmente con AES-GCM (un cifrado estándar que además detecta alteraciones); los
/// demás son los servicios de secretos de cada nube y todavía no están disponibles.
/// </summary>
public sealed class SecretsOptions
{
    public const string SectionName = "Secrets";

    public const string LocalAesGcmProvider = "LocalAesGcm";

    [Required]
    [AllowedValues(LocalAesGcmProvider, "KeyVault", "AwsSecrets", "GcpSecrets")]
    public string Provider { get; set; } = "";

    /// <summary>Ajustes del proveedor <c>LocalAesGcm</c>; solo se exigen cuando ese es el proveedor.</summary>
    public LocalAesGcmSecretsOptions LocalAesGcm { get; set; } = new();
}

/// <summary>
/// Proveedor local (instalación mínima con Docker en un servidor, Arquitectura detallada, sección 13): la llave
/// maestra llega como un secreto montado en un archivo y los certificados cifrados se guardan en una carpeta.
/// </summary>
public sealed class LocalAesGcmSecretsOptions
{
    /// <summary>
    /// Ruta del archivo con la llave maestra: 32 bytes al azar escritos en base64 (por ejemplo, la salida de
    /// <c>openssl rand -base64 32</c>). La ruta llega por variable de entorno
    /// (<c>Secrets__LocalAesGcm__MasterKeyFile</c>) y suele apuntar a un secreto de Docker en
    /// <c>/run/secrets/</c>. La llave nunca va en <c>appsettings</c>; solo su ruta.
    /// </summary>
    public string MasterKeyFile { get; set; } = "";

    /// <summary>Carpeta donde se guardan los certificados cifrados, una subcarpeta por empresa.</summary>
    public string CertificateDirectory { get; set; } = "";
}
