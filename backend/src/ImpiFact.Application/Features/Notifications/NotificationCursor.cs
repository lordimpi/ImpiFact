using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace ImpiFact.Application.Features.Notifications;

/// <summary>
/// Cursor del listado de avisos: la posición <c>(created_at, id)</c> del último aviso de la página, en Base64 apto
/// para URL. Es el mismo formato del cursor de documentos: la página siguiente empieza justo después de esa posición,
/// así no salta ni repite filas aunque lleguen avisos nuevos, y no usa OFFSET. Para el cliente es un texto opaco.
/// </summary>
public static class NotificationCursor
{
    private const char Separator = '.';

    public static string Encode(NotificationKey key)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"{key.CreatedAt.UtcTicks}{Separator}{key.Id:N}");
        return Base64Url.EncodeToString(Encoding.ASCII.GetBytes(text));
    }

    public static bool TryDecode(string? cursor, out NotificationKey key)
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

        key = new NotificationKey(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}
