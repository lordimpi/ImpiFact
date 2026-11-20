using System.ComponentModel.DataAnnotations;

namespace ImpiFact.Infrastructure.Options;

/// <summary>
/// Dónde se guardan los certificados y las claves de las empresas (Arquitectura detallada, sección 11).
/// <c>LocalAesGcm</c> cifra localmente con AES-GCM (un cifrado estándar que además detecta alteraciones); los
/// demás son los servicios de secretos de cada nube (fase 6), que entran con la identidad de la carga de trabajo, sin
/// llaves en la configuración.
/// </summary>
/// <remarks>
/// El proveedor elegido guarda los certificados y protege las llaves de Data Protection (las que cifran los campos
/// <c>*_cipher</c>). Cambiar de proveedor deja ilegibles los certificados y los campos ya guardados con el anterior.
/// </remarks>
public sealed class SecretsOptions
{
    public const string SectionName = "Secrets";

    public const string LocalAesGcmProvider = "LocalAesGcm";

    public const string KeyVaultProvider = "KeyVault";

    public const string AwsSecretsProvider = "AwsSecrets";

    [Required]
    [AllowedValues(LocalAesGcmProvider, KeyVaultProvider, AwsSecretsProvider, "GcpSecrets")]
    public string Provider { get; set; } = "";

    /// <summary>Ajustes del proveedor <c>LocalAesGcm</c>; solo se exigen cuando ese es el proveedor.</summary>
    public LocalAesGcmSecretsOptions LocalAesGcm { get; set; } = new();

    /// <summary>Ajustes del proveedor <c>KeyVault</c>; solo se exigen cuando ese es el proveedor.</summary>
    public KeyVaultSecretsOptions KeyVault { get; set; } = new();

    /// <summary>Ajustes del proveedor <c>AwsSecrets</c>; solo se exigen cuando ese es el proveedor.</summary>
    public AwsSecretsOptions AwsSecrets { get; set; } = new();
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

/// <summary>
/// Proveedor <c>KeyVault</c> (Azure Key Vault; lowkey-vault en desarrollo, perfil <c>keyvault</c> de
/// <c>deploy/docker-compose.yml</c>): cada certificado es un secreto del vault y las llaves de Data Protection se
/// envuelven con una llave RSA del mismo vault.
/// </summary>
/// <remarks>
/// Aquí no hay llaves ni secretos: el cliente usa <c>DefaultAzureCredential</c> (la identidad administrada en Azure). La
/// identidad necesita, sobre el vault, leer, escribir y borrar secretos («Key Vault Secrets Officer») y envolver y
/// desenvolver con la llave («Key Vault Crypto User»).
/// </remarks>
public sealed class KeyVaultSecretsOptions
{
    /// <summary>Dirección del vault, por ejemplo <c>https://impifact.vault.azure.net</c>. Absoluta y <c>https</c>.</summary>
    public string VaultUri { get; set; } = "";

    /// <summary>
    /// Nombre de la llave RSA del vault con que se envuelven las llaves de Data Protection (por ejemplo
    /// <c>impifact-data-protection</c>); la crea el despliegue. Se usa su versión vigente al envolver y la que quedó
    /// anotada al desenvolver, así rotarla no deja ilegible nada.
    /// </summary>
    public string KeyName { get; set; } = "";
}

/// <summary>
/// Proveedor <c>AwsSecrets</c> (AWS Secrets Manager con una llave de AWS KMS; Moto en desarrollo, perfil <c>aws</c> de
/// <c>deploy/docker-compose.yml</c>): cada certificado es un secreto cifrado con la llave de KMS y las llaves de Data
/// Protection se envuelven con la misma llave.
/// </summary>
/// <remarks>
/// Aquí no hay llaves de acceso: los clientes usan la cadena estándar de credenciales de AWS (el rol de la tarea de ECS o
/// de la cuenta de servicio de EKS en la nube; variables <c>AWS_ACCESS_KEY_ID</c> y <c>AWS_SECRET_ACCESS_KEY</c> o un
/// perfil de <c>~/.aws</c> en un equipo de desarrollo).
/// </remarks>
public sealed class AwsSecretsOptions
{
    /// <summary>
    /// Región de AWS, por ejemplo <c>us-east-1</c>. Se exige salvo que haya <see cref="ServiceUrl"/>; en ese caso, si
    /// falta, se firma con <c>us-east-1</c>.
    /// </summary>
    public string Region { get; set; } = "";

    /// <summary>
    /// Solo para un emulador: URL de un servicio compatible con Secrets Manager y KMS (por ejemplo
    /// <c>http://127.0.0.1:5000</c> para Moto). Vacía para usar AWS. Absoluta, <c>http</c> o <c>https</c>, sin usuario,
    /// contraseña, consulta ni fragmento.
    /// </summary>
    public string ServiceUrl { get; set; } = "";

    /// <summary>
    /// La llave simétrica de KMS (su id, su ARN o un alias como <c>alias/impifact-data-protection</c>) que cifra los
    /// secretos de los certificados y envuelve las llaves de Data Protection; la crea el despliegue.
    /// </summary>
    public string KmsKeyId { get; set; } = "";
}
