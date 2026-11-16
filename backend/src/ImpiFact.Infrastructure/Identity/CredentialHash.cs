using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>
/// Valores secretos opacos (API keys y tokens de renovación) y sus resúmenes. Un resumen SHA-256 (función que
/// convierte cualquier texto en 32 bytes y no se puede invertir) permite buscar la credencial en la base sin
/// guardarla: si la base se filtra, los resúmenes no sirven para autenticarse.
/// </summary>
public static class CredentialHash
{
    /// <summary>Bytes aleatorios de cada valor nuevo: 32 bytes son 256 bits de entropía.</summary>
    internal const int EntropyBytes = 32;

    /// <summary>Resumen SHA-256 del texto en UTF-8, en hexadecimal en minúsculas (64 caracteres, <c>char(64)</c>).</summary>
    public static string Sha256Hex(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>
    /// Compara dos resúmenes en tiempo constante: tarda lo mismo sin importar en qué carácter difieren, así una
    /// medición de tiempos no revela cuántos caracteres coinciden.
    /// </summary>
    public static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    /// <summary>
    /// Valor aleatorio nuevo de <see cref="EntropyBytes"/> bytes, generado con el generador criptográfico del
    /// sistema y escrito en Base64 URL (letras, dígitos, «-» y «_», sin relleno; 43 caracteres).
    /// </summary>
    internal static string NewOpaqueValue() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(EntropyBytes));
}
