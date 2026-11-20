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
            default:
                return ValidateOptionsResult.Fail(
                    $"Secrets provider '{options.Provider}' is not available yet; use '{SecretsOptions.LocalAesGcmProvider}' or '{SecretsOptions.KeyVaultProvider}'.");
        }
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
}
