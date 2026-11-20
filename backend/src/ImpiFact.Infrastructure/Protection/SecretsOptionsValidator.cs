using System.Text.RegularExpressions;
using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.Protection;

/// <summary>
/// Validación de <see cref="SecretsOptions"/> que corre al arrancar, junto con sus atributos. Con
/// <c>LocalAesGcm</c> exige la carpeta de certificados y un archivo de llave maestra legible con 32 bytes en
/// base64; con un proveedor de nube, la dirección del servicio y la llave que protege las llaves de Data Protection;
/// si falta algo, la aplicación no arranca.
/// </summary>
internal sealed partial class SecretsOptionsValidator : IValidateOptions<SecretsOptions>
{
    public ValidateOptionsResult Validate(string? name, SecretsOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            // El atributo [Required] ya informa esta falla.
            return ValidateOptionsResult.Skip;
        }

        switch (options.Provider)
        {
            case SecretsOptions.LocalAesGcmProvider:
                return ValidateLocal(options.LocalAesGcm);
            case SecretsOptions.KeyVaultProvider:
                return ValidateKeyVault(options.KeyVault);
            case SecretsOptions.AwsSecretsProvider:
                return ValidateAws(options.AwsSecrets);
            case SecretsOptions.GcpSecretsProvider:
                return ValidateGcp(options.GcpSecrets);
            default:
                // Un proveedor desconocido ya lo rechaza el atributo [AllowedValues].
                return ValidateOptionsResult.Skip;
        }
    }

    private static ValidateOptionsResult ValidateGcp(GcpSecretsOptions gcp)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(gcp.ProjectId))
        {
            failures.Add("Secrets:GcpSecrets:ProjectId is required when Secrets:Provider is GcpSecrets.");
        }
        else if (!GcpProjectId().IsMatch(gcp.ProjectId))
        {
            failures.Add($"Secrets:GcpSecrets:ProjectId '{gcp.ProjectId}' is not a valid Google Cloud project id: use 6 to 30 lower-case letters, digits or '-', starting with a letter.");
        }

        if (string.IsNullOrWhiteSpace(gcp.KmsKeyName))
        {
            failures.Add("Secrets:GcpSecrets:KmsKeyName is required when Secrets:Provider is GcpSecrets: the Cloud KMS key that wraps the Data Protection keys.");
        }
        else if (!GcpKmsKeyName().IsMatch(gcp.KmsKeyName))
        {
            failures.Add(
                $"Secrets:GcpSecrets:KmsKeyName '{gcp.KmsKeyName}' is not a Cloud KMS key name: use projects/<project>/locations/<location>/keyRings/<ring>/cryptoKeys/<key>, without a version.");
        }

        if (!string.IsNullOrWhiteSpace(gcp.Endpoint) && !ServiceEndpoint.IsValidUrl(gcp.Endpoint))
        {
            failures.Add(
                $"Secrets:GcpSecrets:Endpoint {ServiceEndpoint.Describe(gcp.Endpoint)} must be an absolute http or https URL without user information, query or fragment.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static ValidateOptionsResult ValidateAws(AwsSecretsOptions aws)
    {
        var failures = new List<string>();
        var hasServiceUrl = !string.IsNullOrWhiteSpace(aws.ServiceUrl);
        if (hasServiceUrl && !ServiceEndpoint.IsValidUrl(aws.ServiceUrl))
        {
            failures.Add(
                $"Secrets:AwsSecrets:ServiceUrl {ServiceEndpoint.Describe(aws.ServiceUrl)} must be an absolute http or https URL without user information, query or fragment.");
        }

        if (string.IsNullOrWhiteSpace(aws.Region))
        {
            if (!hasServiceUrl)
            {
                failures.Add("Secrets:AwsSecrets:Region is required when Secrets:AwsSecrets:ServiceUrl is not set.");
            }
        }
        else if (!ServiceEndpoint.IsAwsRegion(aws.Region))
        {
            failures.Add($"Secrets:AwsSecrets:Region '{aws.Region}' is not a valid AWS region, for example 'us-east-1'.");
        }

        if (string.IsNullOrWhiteSpace(aws.KmsKeyId))
        {
            failures.Add("Secrets:AwsSecrets:KmsKeyId is required when Secrets:Provider is AwsSecrets: the KMS key that encrypts the certificates and wraps the Data Protection keys.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static ValidateOptionsResult ValidateKeyVault(KeyVaultSecretsOptions keyVault)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(keyVault.VaultUri))
        {
            failures.Add("Secrets:KeyVault:VaultUri is required when Secrets:Provider is KeyVault.");
        }
        else if (!ServiceEndpoint.IsValidHttpsUrl(keyVault.VaultUri))
        {
            failures.Add(
                $"Secrets:KeyVault:VaultUri {ServiceEndpoint.Describe(keyVault.VaultUri)} must be an absolute https URL without user information, query or fragment, for example 'https://impifact.vault.azure.net'.");
        }

        if (string.IsNullOrWhiteSpace(keyVault.KeyName))
        {
            failures.Add("Secrets:KeyVault:KeyName is required when Secrets:Provider is KeyVault: the RSA key that wraps the Data Protection keys.");
        }
        else if (!KeyVaultName().IsMatch(keyVault.KeyName))
        {
            failures.Add($"Secrets:KeyVault:KeyName '{keyVault.KeyName}' is not a valid Key Vault key name: use 1 to 127 letters, digits or '-'.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static ValidateOptionsResult ValidateLocal(LocalAesGcmSecretsOptions local)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(local.MasterKeyFile))
        {
            failures.Add("Secrets:LocalAesGcm:MasterKeyFile is required when Secrets:Provider is LocalAesGcm.");
        }
        else if (LocalMasterKey.Validate(local.MasterKeyFile) is { } failure)
        {
            failures.Add(failure);
        }

        if (string.IsNullOrWhiteSpace(local.CertificateDirectory))
        {
            failures.Add("Secrets:LocalAesGcm:CertificateDirectory is required when Secrets:Provider is LocalAesGcm.");
        }
        else if (!CanWrite(local.CertificateDirectory))
        {
            failures.Add($"Secrets:LocalAesGcm:CertificateDirectory '{local.CertificateDirectory}' cannot be created or written.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    // Crea la carpeta (con permisos solo para el dueño en Linux) y escribe y borra un archivo de prueba: así una
    // carpeta mal montada o de solo lectura falla al arrancar y no en la primera carga de un certificado.
    private static bool CanWrite(string directory)
    {
        try
        {
            LocalAesGcmCertificateStore.CreatePrivateDirectory(directory);
            var probe = Path.Combine(directory, ".write-check-" + Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Nombres de Key Vault: de 1 a 127 letras, dígitos o «-».
    [GeneratedRegex(@"^[0-9A-Za-z-]{1,127}\z", RegexOptions.CultureInvariant)]
    private static partial Regex KeyVaultName();

    // Identificador de proyecto de Google Cloud: de 6 a 30 minúsculas, dígitos o «-»; empieza con letra y no termina en «-».
    [GeneratedRegex(@"^[a-z][a-z0-9-]{4,28}[a-z0-9]\z", RegexOptions.CultureInvariant)]
    private static partial Regex GcpProjectId();

    // projects/<proyecto>/locations/<ubicación>/keyRings/<anillo>/cryptoKeys/<llave>, sin versión.
    [GeneratedRegex(@"^projects/[^/]+/locations/[^/]+/keyRings/[^/]+/cryptoKeys/[^/]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex GcpKmsKeyName();
}
