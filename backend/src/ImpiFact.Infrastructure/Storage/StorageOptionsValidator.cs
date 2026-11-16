using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.Storage;

/// <summary>
/// Validación de <see cref="StorageOptions"/> que corre al arrancar, junto con sus atributos. Con <c>Local</c> exige
/// una carpeta raíz con ruta absoluta que se pueda crear y escribir; si no, la aplicación no arranca. Los
/// proveedores de nube todavía no existen: elegirlos también detiene el arranque.
/// </summary>
internal sealed class StorageOptionsValidator : IValidateOptions<StorageOptions>
{
    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            // El atributo [Required] ya informa esta falla.
            return ValidateOptionsResult.Skip;
        }

        if (!string.Equals(options.Provider, StorageOptions.LocalProvider, StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                $"Storage provider '{options.Provider}' is not available yet; use '{StorageOptions.LocalProvider}'.");
        }

        var root = options.Local.Root;
        if (string.IsNullOrWhiteSpace(root))
        {
            return ValidateOptionsResult.Fail("Storage:Local:Root is required when Storage:Provider is Local.");
        }

        if (!Path.IsPathFullyQualified(root))
        {
            return ValidateOptionsResult.Fail($"Storage:Local:Root '{root}' must be an absolute path.");
        }

        return CanWrite(root)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"Storage:Local:Root '{root}' cannot be created or written.");
    }

    // Crea la carpeta y escribe y borra un archivo de prueba: una carpeta mal montada o de solo lectura falla al
    // arrancar y no al guardar el primer XML.
    private static bool CanWrite(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
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
}
