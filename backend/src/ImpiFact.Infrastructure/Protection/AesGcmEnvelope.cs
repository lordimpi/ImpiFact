using System.Security.Cryptography;

namespace ImpiFact.Infrastructure.Protection;

/// <summary>
/// Sobre cifrado con AES-256-GCM. AES-GCM es un cifrado autenticado: además de ocultar el contenido, produce una
/// etiqueta (tag) de 16 bytes que detecta cualquier cambio, y descifrar un sobre alterado falla. Cada cifrado usa
/// un nonce (número que se usa una sola vez) de 96 bits al azar; repetirlo con la misma llave rompería la
/// seguridad, por eso nunca se reutiliza. Los datos asociados (associated data) no se cifran ni se guardan, pero
/// entran en la etiqueta: si al descifrar no son los mismos, falla. Así el sobre queda atado, por ejemplo, a su
/// empresa.
/// </summary>
/// <remarks>Formato: versión (1 byte) | nonce (12 bytes) | etiqueta (16 bytes) | texto cifrado.</remarks>
internal static class AesGcmEnvelope
{
    private const byte Version = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 1 + NonceSize + TagSize;

    public static byte[] Seal(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData)
    {
        var envelope = new byte[HeaderSize + plaintext.Length];
        envelope[0] = Version;
        var nonce = envelope.AsSpan(1, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, envelope.AsSpan(HeaderSize), envelope.AsSpan(1 + NonceSize, TagSize), associatedData);
        return envelope;
    }

    /// <summary>Descifra un sobre. Quien recibe el resultado lo borra al terminar.</summary>
    /// <exception cref="CryptographicException">Si el sobre fue alterado, no es de esta versión o los datos asociados no coinciden.</exception>
    public static byte[] Open(ReadOnlySpan<byte> key, ReadOnlySpan<byte> envelope, ReadOnlySpan<byte> associatedData)
    {
        if (envelope.Length < HeaderSize || envelope[0] != Version)
        {
            throw new CryptographicException("The encrypted content has an unknown format.");
        }

        var plaintext = new byte[envelope.Length - HeaderSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(envelope.Slice(1, NonceSize), envelope[HeaderSize..], envelope.Slice(1 + NonceSize, TagSize), plaintext, associatedData);
            return plaintext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
    }
}
