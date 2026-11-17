using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImpiFact.Application.Features.Billing.StorePlan;

/// <summary>
/// Lee un booleano como lo acepta la regla <c>boolean</c> de Laravel en el PHP: <c>true</c>, <c>false</c>, 1, 0, "1",
/// "0", "true" y "false". La colección Postman manda el estado del plan como <c>"state": 1</c>. Al escribir sale como
/// booleano JSON.
/// </summary>
public sealed class LaravelBooleanConverter : JsonConverter<bool?>
{
    public override bool HandleNull => true;

    public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.True => true,
        JsonTokenType.False => false,
        JsonTokenType.Null => null,
        JsonTokenType.Number when reader.TryGetInt32(out var number) && number is 0 or 1 => number == 1,
        JsonTokenType.String => reader.GetString() switch
        {
            "1" or "true" => true,
            "0" or "false" => false,
            var text => throw new JsonException($"'{text}' is not a boolean."),
        },
        _ => throw new JsonException($"Expected a boolean but found {reader.TokenType}."),
    };

    public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
    {
        if (value is { } flag)
        {
            writer.WriteBooleanValue(flag);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
