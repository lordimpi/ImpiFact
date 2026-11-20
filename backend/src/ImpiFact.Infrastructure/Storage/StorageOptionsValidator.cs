using System.Text.RegularExpressions;
using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.Storage;

/// <summary>
/// Validación de <see cref="StorageOptions"/> que corre al arrancar, junto con sus atributos. Con <c>Local</c> exige
/// una carpeta raíz con ruta absoluta que se pueda crear y escribir; con <c>S3</c>, un bucket con nombre válido,
/// una región (o una URL de servicio compatible) y una vigencia máxima de URL prefirmada dentro del rango; con
/// <c>AzureBlob</c>, la dirección de la cuenta y un contenedor con nombre válido; con <c>Gcs</c>, un bucket con nombre
/// válido; si no, la aplicación no arranca.
/// </summary>
internal sealed partial class StorageOptionsValidator(IEnumerable<ReservedDirectory> reserved) : IValidateOptions<StorageOptions>
{
    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            // El atributo [Required] ya informa esta falla.
            return ValidateOptionsResult.Skip;
        }

        if (string.Equals(options.Provider, StorageOptions.S3Provider, StringComparison.Ordinal))
        {
            return ValidateS3(options.S3);
        }

        if (string.Equals(options.Provider, StorageOptions.AzureBlobProvider, StringComparison.Ordinal))
        {
            return ValidateAzureBlob(options.AzureBlob);
        }

        if (string.Equals(options.Provider, StorageOptions.GcsProvider, StringComparison.Ordinal))
        {
            return ValidateGcs(options.Gcs);
        }

        // Un proveedor desconocido ya lo rechaza el atributo [AllowedValues].
        return string.Equals(options.Provider, StorageOptions.LocalProvider, StringComparison.Ordinal)
            ? ValidateLocal(options)
            : ValidateOptionsResult.Skip;
    }

    private ValidateOptionsResult ValidateLocal(StorageOptions options)
    {
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

    private static ValidateOptionsResult ValidateS3(S3StorageOptions s3)
    {
        if (string.IsNullOrWhiteSpace(s3.Bucket))
        {
            return ValidateOptionsResult.Fail("Storage:S3:Bucket is required when Storage:Provider is S3.");
        }

        if (!BucketName().IsMatch(s3.Bucket) || s3.Bucket.Contains("..", StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                $"Storage:S3:Bucket '{s3.Bucket}' is not a valid S3 bucket name: use 3 to 63 lower-case letters, digits, '.' or '-'.");
        }

        var hasServiceUrl = !string.IsNullOrWhiteSpace(s3.ServiceUrl);
        if (hasServiceUrl && !ServiceEndpoint.IsValidUrl(s3.ServiceUrl))
        {
            return ValidateOptionsResult.Fail(
                $"Storage:S3:ServiceUrl '{s3.ServiceUrl}' must be an absolute http or https URL without user information, query or fragment.");
        }

        if (string.IsNullOrWhiteSpace(s3.Region))
        {
            if (!hasServiceUrl)
            {
                return ValidateOptionsResult.Fail("Storage:S3:Region is required when Storage:S3:ServiceUrl is not set.");
            }
        }
        else if (!ServiceEndpoint.IsAwsRegion(s3.Region))
        {
            return ValidateOptionsResult.Fail($"Storage:S3:Region '{s3.Region}' is not a valid AWS region, for example 'us-east-1'.");
        }

        if (s3.SignedUrlMaxMinutes is < 1 or > S3StorageOptions.SignedUrlLimitMinutes)
        {
            return ValidateOptionsResult.Fail(
                $"Storage:S3:SignedUrlMaxMinutes must be between 1 and {S3StorageOptions.SignedUrlLimitMinutes}.");
        }

        return ValidateOptionsResult.Success;
    }

    private static ValidateOptionsResult ValidateAzureBlob(AzureBlobStorageOptions azure)
    {
        if (string.IsNullOrWhiteSpace(azure.ServiceUri))
        {
            return ValidateOptionsResult.Fail("Storage:AzureBlob:ServiceUri is required when Storage:Provider is AzureBlob.");
        }

        // Sin SAS ni llave en la URL: el acceso sale de la identidad de la carga de trabajo, nunca de la configuración.
        if (!ServiceEndpoint.IsValidUrl(azure.ServiceUri))
        {
            return ValidateOptionsResult.Fail(
                $"Storage:AzureBlob:ServiceUri {ServiceEndpoint.Describe(azure.ServiceUri)} must be an absolute http or https URL without user information, query or fragment.");
        }

        if (string.IsNullOrWhiteSpace(azure.Container))
        {
            return ValidateOptionsResult.Fail("Storage:AzureBlob:Container is required when Storage:Provider is AzureBlob.");
        }

        if (!ContainerName().IsMatch(azure.Container))
        {
            return ValidateOptionsResult.Fail(
                $"Storage:AzureBlob:Container '{azure.Container}' is not a valid Azure Blob container name: use 3 to 63 lower-case letters, digits or single '-'.");
        }

        if (azure.UseEmulatorAccount && !AzureBlobStorage.IsEmulatorEndpoint(new Uri(azure.ServiceUri)))
        {
            return ValidateOptionsResult.Fail(
                "Storage:AzureBlob:UseEmulatorAccount only works with Azurite: Storage:AzureBlob:ServiceUri must be http://<host>:10000/devstoreaccount1.");
        }

        if (azure.SignedUrlMaxMinutes is < 1 or > AzureBlobStorageOptions.SignedUrlLimitMinutes)
        {
            return ValidateOptionsResult.Fail(
                $"Storage:AzureBlob:SignedUrlMaxMinutes must be between 1 and {AzureBlobStorageOptions.SignedUrlLimitMinutes}.");
        }

        return ValidateOptionsResult.Success;
    }

    private static ValidateOptionsResult ValidateGcs(GcsStorageOptions gcs)
    {
        if (string.IsNullOrWhiteSpace(gcs.Bucket))
        {
            return ValidateOptionsResult.Fail("Storage:Gcs:Bucket is required when Storage:Provider is Gcs.");
        }

        if (!GcsBucketName().IsMatch(gcs.Bucket) || gcs.Bucket.StartsWith("goog", StringComparison.Ordinal) || gcs.Bucket.Contains("..", StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                $"Storage:Gcs:Bucket '{gcs.Bucket}' is not a valid Cloud Storage bucket name: use 3 to 63 lower-case letters, digits, '-', '_' or '.', not starting with 'goog'.");
        }

        if (!string.IsNullOrWhiteSpace(gcs.Endpoint) && !ServiceEndpoint.IsValidUrl(gcs.Endpoint))
        {
            return ValidateOptionsResult.Fail(
                $"Storage:Gcs:Endpoint {ServiceEndpoint.Describe(gcs.Endpoint)} must be an absolute http or https URL without user information, query or fragment.");
        }

        if (gcs.SignedUrlMaxMinutes is < 1 or > GcsStorageOptions.SignedUrlLimitMinutes)
        {
            return ValidateOptionsResult.Fail(
                $"Storage:Gcs:SignedUrlMaxMinutes must be between 1 and {GcsStorageOptions.SignedUrlLimitMinutes}.");
        }

        return ValidateOptionsResult.Success;
    }

    // Reglas de nombres de bucket de AWS: de 3 a 63 caracteres, minúsculas, dígitos, «.» o «-», y empieza y termina
    // con letra o dígito.
    [GeneratedRegex(@"^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]\z", RegexOptions.CultureInvariant)]
    private static partial Regex BucketName();

    // Reglas de nombres de contenedor de Azure: de 3 a 63 caracteres, minúsculas, dígitos o «-», empieza y termina con
    // letra o dígito y nunca lleva dos «-» seguidos.
    [GeneratedRegex(@"^(?!.*--)[a-z0-9][a-z0-9-]{1,61}[a-z0-9]\z", RegexOptions.CultureInvariant)]
    private static partial Regex ContainerName();

    // Reglas de nombres de bucket de Google Cloud: de 3 a 63 caracteres, minúsculas, dígitos, «-», «_» o «.», y empieza y
    // termina con letra o dígito.
    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]{1,61}[a-z0-9]\z", RegexOptions.CultureInvariant)]
    private static partial Regex GcsBucketName();

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
