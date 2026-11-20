using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ImpiFact.Infrastructure.Protection;

/// <summary>
/// Contenido que guardan los almacenes de certificados: el PKCS#12 (el archivo .p12 o .pfx con el certificado y su llave
/// privada) junto con su contraseña, en una sola pieza: largo de la contraseña (4 bytes) | contraseña en UTF-8 |
/// PKCS#12. <c>LocalAesGcm</c> lo cifra con su sobre AES-256-GCM; los proveedores de nube lo guardan como el valor de un
/// secreto, que el servicio cifra con sus propias llaves.
/// </summary>
internal static class CertificatePayload
{
    /// <summary>Arma el contenido. Quien lo recibe lo borra con <see cref="CryptographicOperations.ZeroMemory"/> al terminar.</summary>
    public static byte[] Pack(byte[] pfx, string password)
    {
        ArgumentNullException.ThrowIfNull(pfx);
        ArgumentNullException.ThrowIfNull(password);

        var passwordLength = Encoding.UTF8.GetByteCount(password);
        var payload = new byte[sizeof(int) + passwordLength + pfx.Length];
        BinaryPrimitives.WriteInt32BigEndian(payload, passwordLength);
        Encoding.UTF8.GetBytes(password, payload.AsSpan(sizeof(int)));
        pfx.CopyTo(payload.AsSpan(sizeof(int) + passwordLength));
        return payload;
    }

    /// <summary>
    /// Abre el certificado solo en memoria (<see cref="X509KeyStorageFlags.EphemeralKeySet"/>: la llave privada no pasa
    /// por el almacén del sistema). La contraseña intermedia se borra; el contenido lo borra quien lo pasó.
    /// </summary>
    /// <exception cref="CryptographicException">Si el contenido no tiene la forma esperada.</exception>
    public static X509Certificate2 Load(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < sizeof(int))
        {
            throw new CryptographicException("The stored certificate has an unknown format.");
        }

        var passwordLength = BinaryPrimitives.ReadInt32BigEndian(payload);
        if (passwordLength < 0 || passwordLength > payload.Length - sizeof(int))
        {
            throw new CryptographicException("The stored certificate has an unknown format.");
        }

        var passwordBytes = payload.Slice(sizeof(int), passwordLength);
        var password = new char[Encoding.UTF8.GetCharCount(passwordBytes)];
        try
        {
            Encoding.UTF8.GetChars(passwordBytes, password);
            return X509CertificateLoader.LoadPkcs12(payload[(sizeof(int) + passwordLength)..], password, X509KeyStorageFlags.EphemeralKeySet);
        }
        finally
        {
            Array.Clear(password);
        }
    }
}
