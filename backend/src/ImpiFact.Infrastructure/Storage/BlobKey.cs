using System.Text.RegularExpressions;

namespace ImpiFact.Infrastructure.Storage;

/// <summary>
/// Forma válida de una clave de archivo (ver <c>IBlobStorage</c>): tramos separados por «/», cada uno empieza con
/// una letra o un dígito, solo lleva letras, dígitos, «.», «_» o «-» y no termina en «.»; en total, hasta 500
/// caracteres (el largo de <c>document_files.storage_key</c>). Así una clave nunca es absoluta ni sube de carpeta
/// con «..», en ningún sistema operativo. También se rechazan los nombres que Windows reserva para dispositivos
/// (<c>CON</c>, <c>NUL</c>, <c>COM1</c>…), que allí no abren un archivo.
/// </summary>
internal static partial class BlobKey
{
    public const int MaxLength = 500;

    /// <exception cref="ArgumentException">Si la clave no tiene la forma válida.</exception>
    public static void Validate(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length is 0 or > MaxLength || !key.Split('/').All(IsValidSegment))
        {
            throw new ArgumentException(
                $"Invalid storage key '{key}': use relative segments of letters, digits, '.', '_' or '-' separated by '/'.",
                nameof(key));
        }
    }

    private static bool IsValidSegment(string segment) => Segment().IsMatch(segment) && !ReservedDeviceName().IsMatch(segment);

    // \z (y no $) para que un salto de línea al final no pase como válido.
    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9_-])?\z", RegexOptions.CultureInvariant)]
    private static partial Regex Segment();

    [GeneratedRegex(@"^(?:CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\..*)?\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ReservedDeviceName();
}
