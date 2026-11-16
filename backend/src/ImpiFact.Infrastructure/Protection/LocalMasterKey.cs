using System.Security.Cryptography;
using System.Text;
using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.Protection;

/// <summary>
/// Llave maestra del proveedor <c>LocalAesGcm</c>: 32 bytes (256 bits) al azar que llegan en un archivo montado
/// como secreto (por ejemplo, un secreto de Docker), escritos en base64. Nunca se usa directamente para cifrar:
/// de ella se deriva una llave distinta por cada uso con HKDF (una función estándar que produce llaves
/// independientes a partir de una llave madre y una etiqueta), así una llave comprometida en un uso no sirve
/// para otro.
/// </summary>
/// <remarks>
/// El archivo se lee la primera vez que se necesita, cuando la validación del arranque ya comprobó que existe
/// y es válido. Al liberar el servicio, la llave se borra de la memoria.
/// </remarks>
internal sealed class LocalMasterKey : IDisposable
{
    public const int KeySize = 32;

    private readonly Lazy<byte[]> key;

    public LocalMasterKey(IOptions<SecretsOptions> options)
    {
        key = new Lazy<byte[]>(() => Read(options.Value.LocalAesGcm.MasterKeyFile), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Deriva la llave de un uso (por ejemplo, <c>impifact/certificates/v1</c>). Quien la recibe la borra con
    /// <see cref="CryptographicOperations.ZeroMemory"/> al terminar.
    /// </summary>
    public byte[] DeriveKey(string purpose) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, key.Value, KeySize, salt: [], info: Encoding.UTF8.GetBytes(purpose));

    /// <summary>
    /// Revisa el archivo de la llave sin quedarse con ella. Devuelve el motivo de la falla, o <c>null</c> si es
    /// válido. El mensaje nunca incluye el contenido del archivo.
    /// </summary>
    public static string? Validate(string path)
    {
        try
        {
            CryptographicOperations.ZeroMemory(Read(path));
            return null;
        }
        catch (InvalidMasterKeyException exception)
        {
            return exception.Message;
        }
    }

    public void Dispose()
    {
        if (key.IsValueCreated)
        {
            CryptographicOperations.ZeroMemory(key.Value);
        }
    }

    private static byte[] Read(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path).Trim();
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new InvalidMasterKeyException($"The master key file '{path}' does not exist.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidMasterKeyException($"The master key file '{path}' could not be read.");
        }

        // El búfer alcanza para cualquier base64 de ese largo; luego se exige que decodifique a 32 bytes exactos.
        var buffer = new byte[text.Length];
        try
        {
            if (!Convert.TryFromBase64String(text, buffer, out var written) || written != KeySize)
            {
                throw new InvalidMasterKeyException(
                    $"The master key file '{path}' must contain exactly {KeySize} bytes encoded in base64.");
            }

            return buffer.AsSpan(0, KeySize).ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private sealed class InvalidMasterKeyException(string message) : Exception(message);
}
