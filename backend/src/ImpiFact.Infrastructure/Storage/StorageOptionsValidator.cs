using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.Storage;

/// <summary>
/// Validación de <see cref="StorageOptions"/> que corre al arrancar, junto con sus atributos. Con <c>Local</c> exige
/// una carpeta raíz con ruta absoluta que se pueda crear y escribir; si no, la aplicación no arranca. Los
/// proveedores de nube todavía no existen: elegirlos también detiene el arranque.
/// </summary>
internal sealed class StorageOptionsValidator(IEnumerable<ReservedDirectory> reserved) : IValidateOptions<StorageOptions>
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

        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (string.Equals(fullRoot, Path.TrimEndingDirectorySeparator(Path.GetPathRoot(fullRoot) ?? ""), StringComparison.OrdinalIgnoreCase))
        {
            // En la raíz de un disco ninguna llave cabría dentro de la raíz, y escribir ahí no es seguro.
            return ValidateOptionsResult.Fail($"Storage:Local:Root '{root}' must not be a filesystem root.");
        }

        // Los certificados cifrados no pueden quedar al alcance del almacenamiento de documentos, ni al revés.
        foreach (var directory in reserved)
        {
            if (!string.IsNullOrWhiteSpace(directory.Path) && Path.IsPathFullyQualified(directory.Path) && Overlap(fullRoot, directory.Path))
            {
                return ValidateOptionsResult.Fail($"Storage:Local:Root '{root}' and {directory.Setting} must not overlap.");
            }
        }

        return CanWrite(root)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"Storage:Local:Root '{root}' cannot be created or written.");
    }

    // Se comparan sin distinguir mayúsculas: en Windows y macOS «A» y «a» son la misma carpeta.
    private static bool Overlap(string first, string second)
    {
        var a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)) + Path.DirectorySeparatorChar;
        var b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)) + Path.DirectorySeparatorChar;
        return a.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
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

/// <summary>
/// Carpeta que el almacenamiento de documentos no puede tocar, con el nombre del dato de configuración que la define.
/// Se registra leyendo la configuración en crudo, sin pasar por la validación de otras opciones.
/// </summary>
internal sealed record ReservedDirectory(string Setting, string? Path);
