using System.ComponentModel.DataAnnotations;

namespace ImpiFact.Infrastructure.Options;

/// <summary>
/// Dónde se guardan los certificados y las claves de las empresas (Arquitectura detallada, sección 11).
/// <c>LocalAesGcm</c> cifra localmente con AES-GCM (un cifrado estándar que además detecta alteraciones); los
/// demás son los servicios de secretos de cada nube. La llave maestra local es un secreto y llega en la tarea de
/// Company, por variable de entorno.
/// </summary>
public sealed class SecretsOptions
{
    public const string SectionName = "Secrets";

    [Required]
    [AllowedValues("LocalAesGcm", "KeyVault", "AwsSecrets", "GcpSecrets")]
    public string Provider { get; set; } = "";
}
