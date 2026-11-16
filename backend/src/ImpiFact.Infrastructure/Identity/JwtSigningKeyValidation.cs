using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>
/// Validación al arrancar de la llave de firma de los JWT (JSON Web Token). Depende del ambiente, por eso no es un
/// atributo de <see cref="JwtOptions"/>: fuera de Development la llave es obligatoria; en Development puede faltar
/// y se usa una temporal (<see cref="JwtSigningKeys"/>). Los mensajes nunca incluyen el valor de la llave.
/// </summary>
internal sealed class JwtSigningKeyValidation(IHostEnvironment environment) : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            if (!environment.IsDevelopment())
            {
                failures.Add($"{JwtOptions.SectionName}:{nameof(JwtOptions.SigningKey)} is required outside Development. "
                    + "Provide a PEM-encoded P-256 private key through the Jwt__SigningKey environment variable or the secrets provider.");
            }
        }
        else
        {
            using var key = EcdsaPem.TryImportPrivate(options.SigningKey);
            if (key is null)
            {
                failures.Add($"{JwtOptions.SectionName}:{nameof(JwtOptions.SigningKey)} is not a valid PEM-encoded P-256 (ES256) private key.");
            }
        }

        for (var index = 0; index < options.PreviousPublicKeys.Length; index++)
        {
            using var key = EcdsaPem.TryImportPublic(options.PreviousPublicKeys[index]);
            if (key is null)
            {
                failures.Add($"{JwtOptions.SectionName}:{nameof(JwtOptions.PreviousPublicKeys)}:{index} is not a valid PEM-encoded P-256 public key.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
