using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Domain.Documents;
using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.Storage;

/// <summary>
/// Almacenamiento de archivos en una carpeta local (<c>Storage:Local:Root</c>; supuesto B3 de la fase 3). Cada
/// clave es una ruta relativa dentro de la raíz; las claves con otra forma se rechazan (<see cref="BlobKey"/>) y,
/// como segunda barrera, la ruta final se compara con la raíz antes de tocar el disco.
/// </summary>
/// <remarks>
/// La escritura es atómica: el contenido va primero a un archivo temporal en la misma carpeta, se baja al disco y
/// luego se renombra sobre el definitivo. Renombrar dentro de un mismo disco es una sola operación del sistema de
/// archivos, así nadie ve un archivo a medias aunque el proceso caiga en mitad de la escritura. El temporal empieza
/// con «.», que ninguna clave válida puede usar, así nunca choca con un archivo guardado.
/// </remarks>
internal sealed class LocalBlobStorage : IBlobStorage
{
    private readonly string root;

    public LocalBlobStorage(IOptions<StorageOptions> options)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Value.Local.Root));
    }

    public StorageProvider Provider => StorageProvider.Local;

    public async Task<string> PutAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = PathOf(key);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await content.CopyToAsync(file, ct);
                await file.FlushAsync(ct);
                // Baja los datos al disco antes de renombrar: si se va la luz, el renombre no deja un archivo vacío.
                file.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        return key;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct)
    {
        var path = PathOf(key);
        try
        {
            Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            return Task.FromResult(stream);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new BlobNotFoundException(key);
        }
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var path = PathOf(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Uri CreateSignedUrl(string key, TimeSpan lifetime) =>
        throw new NotSupportedException("Local storage cannot sign URLs; serve the file through the API instead.");

    private string PathOf(string key)
    {
        BlobKey.Validate(key);
        var path = Path.GetFullPath(Path.Combine(root, key));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Storage key '{key}' points outside the storage root.", nameof(key));
        }

        return path;
    }
}
