using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.Protection;

/// <summary>
/// Validación de <see cref="SecretsOptions"/> que corre al arrancar, junto con sus atributos. Con
/// <c>LocalAesGcm</c> exige la carpeta de certificados y un archivo de llave maestra legible con 32 bytes en
/// base64; si falta algo, la aplicación no arranca. Los proveedores de nube todavía no existen: elegirlos también
/// detiene el arranque, para no guardar llaves sin cifrar.
/// </summary>
internal sealed class SecretsOptionsValidator : IValidateOptions<SecretsOptions>
{
    public ValidateOptionsResult Validate(string? name, SecretsOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            // El atributo [Required] ya informa esta falla.
            return ValidateOptionsResult.Skip;
        }

        if (!string.Equals(options.Provider, SecretsOptions.LocalAesGcmProvider, StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                $"Secrets provider '{options.Provider}' is not available yet; use '{SecretsOptions.LocalAesGcmProvider}'.");
        }

        var failures = new List<string>();
        var local = options.LocalAesGcm;
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

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
