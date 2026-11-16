using System.Globalization;
using System.Text.Json;
using ImpiFact.Dian.Rounding;

namespace ImpiFact.Golden.Tests;

/// <summary>
/// Lectura de las solicitudes JSON de ApiDian (el PHP original) tal como las interpreta PHP: los números
/// pueden llegar como texto o como número, y un valor opcional cuenta como vacío según las reglas de PHP
/// (ver <see cref="TruthyDecimal"/>).
/// </summary>
internal static class ApiDianJson
{
    public static List<T> MapList<T>(JsonElement parent, string name, Func<JsonElement, T> map) =>
        parent.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(map).ToList()
            : [];

    /// <summary>Texto de una propiedad de cadena o numérica; null cuando falta o es null.</summary>
    public static string? OptionalText(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => throw new InvalidDataException($"'{name}' must be a string or a number"),
        };
    }

    public static string RequiredText(JsonElement parent, string name) =>
        OptionalText(parent, name) ?? throw new InvalidDataException($"'{name}' is required");

    public static int RequiredInt(JsonElement parent, string name) =>
        int.Parse(RequiredText(parent, name), CultureInfo.InvariantCulture);

    public static decimal RequiredDecimal(JsonElement parent, string name) => DianRounding.Parse(RequiredText(parent, name));

    public static DateOnly RequiredDate(JsonElement parent, string name) =>
        DateOnly.ParseExact(RequiredText(parent, name), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Devuelve el valor solo si PHP lo considera "no vacío"; si no, null. Las plantillas de ApiDian escriben
    /// el elemento con un @if sobre el valor tal como llegó en la solicitud, y PHP cuenta como falso un valor
    /// faltante, null, "", "0" y el número 0. En cambio, el texto "0.00" cuenta como verdadero.
    /// </summary>
    public static decimal? TruthyDecimal(JsonElement parent, string name)
    {
        var text = OptionalText(parent, name);
        if (string.IsNullOrEmpty(text) || text == "0")
        {
            return null;
        }

        var value = DianRounding.Parse(text);
        var isJsonNumber = parent.GetProperty(name).ValueKind == JsonValueKind.Number;
        return isJsonNumber && value == 0m ? null : value;
    }
}
