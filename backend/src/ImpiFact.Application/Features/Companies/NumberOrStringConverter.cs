using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImpiFact.Application.Features.Companies;

/// <summary>
/// Lee un texto que el contrato JSON heredado a veces envía como número: el teléfono (<c>"phone": 3216547</c>), el
/// PIN o el número de resolución. El número se toma tal como viene escrito, sin cambiarlo; al escribir siempre sale
/// como texto.
/// </summary>
public sealed class NumberOrStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.String => reader.GetString(),
        JsonTokenType.Number => reader.HasValueSequence
            ? Encoding.UTF8.GetString(reader.ValueSequence)
            : Encoding.UTF8.GetString(reader.ValueSpan),
        JsonTokenType.Null => null,
        _ => throw new JsonException($"Expected a string or a number but found {reader.TokenType.ToString().ToLower(CultureInfo.InvariantCulture)}."),
    };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}
