using System.Buffers.Text;
using System.Globalization;
using System.Text;
using ImpiFact.Application.Abstractions.Documents;

namespace ImpiFact.Application.Features.Documents.Queries;

/// <summary>
/// Cursor del listado: la posición <c>(issued_at, id)</c> del último documento de la página, en Base64 apto para URL.
/// La página siguiente empieza justo después de esa posición, así el listado no salta ni repite filas aunque entren
/// documentos nuevos, y no usa OFFSET (Modelo de datos detallado, sección 6). Para el cliente es un texto opaco.
/// </summary>
public static class DocumentCursor
{
    private const char Separator = '.';

    public static string Encode(DocumentKey key)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"{key.IssuedAt.UtcTicks}{Separator}{key.Id:N}");
        return Base64Url.EncodeToString(Encoding.ASCII.GetBytes(text));
    }

    public static bool TryDecode(string? cursor, out DocumentKey key)
    {
        key = default;
        if (string.IsNullOrEmpty(cursor) || cursor.Length > 128 || !Base64Url.IsValid(cursor))
        {
            return false;
        }

        var parts = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(cursor)).Split(Separator);
        if (parts.Length != 2
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTimeOffset.MaxValue.UtcTicks
            || !Guid.TryParseExact(parts[1], "N", out var id))
        {
            return false;
        }

        key = new DocumentKey(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}
