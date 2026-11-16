using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImpiFact.Dian.Rounding;

namespace ImpiFact.Application.Features.Documents.Issue;

/// <summary>Tipo de valor JSON de un <see cref="LegacyScalar"/>.</summary>
public enum LegacyScalarKind
{
    String,
    Number,
    True,
    False,
}

/// <summary>
/// Un valor simple del contrato JSON heredado tal como llegó: el contrato manda los números a veces como texto
/// (<c>"amount": "100.00"</c>) y a veces como número (<c>"number": 990000001</c>), y el PHP (ApiDian, la implementación
/// original) los trata igual. Se conserva el texto exacto y el tipo JSON porque PHP decide si un valor opcional
/// «está vacío» según los dos (ver <see cref="IsPhpEmpty"/>), y eso cambia el XML.
/// </summary>
/// <param name="Text">El texto de la cadena, o el número tal como está escrito en el JSON.</param>
[JsonConverter(typeof(LegacyScalarConverter))]
public readonly record struct LegacyScalar(string Text, LegacyScalarKind Kind)
{
    private const NumberStyles IntegerStyles = NumberStyles.AllowLeadingSign;

    /// <summary>Valor máximo que se acepta en un importe o una cantidad; cabe en las columnas numéricas del modelo.</summary>
    public const decimal MaxMagnitude = 999_999_999_999m;

    /// <summary>
    /// Verdadero si PHP lo considera vacío (falso en un <c>@if</c> de las plantillas): <c>""</c>, <c>"0"</c>, el
    /// número 0 y <c>false</c>. El texto <c>"0.00"</c>, en cambio, cuenta como no vacío.
    /// </summary>
    public bool IsPhpEmpty => Kind switch
    {
        LegacyScalarKind.String => Text.Length == 0 || Text == "0",
        LegacyScalarKind.Number => TryDecimal(out var value) && value == 0m,
        LegacyScalarKind.False => true,
        _ => false,
    };

    /// <summary>Lee un número decimal escrito con punto (como lo lee el redondeo de la DIAN), sin exponente.</summary>
    public bool TryDecimal(out decimal value)
    {
        value = 0m;
        if (Kind is not (LegacyScalarKind.String or LegacyScalarKind.Number) || Text.Length == 0)
        {
            return false;
        }

        try
        {
            value = DianRounding.Parse(Text.Trim());
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>Lee un entero (la regla <c>integer</c> de Laravel, el framework del PHP).</summary>
    public bool TryInteger(out long value)
    {
        value = 0;
        return Kind is LegacyScalarKind.String or LegacyScalarKind.Number
            && long.TryParse(Text.Trim(), IntegerStyles, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Lee un booleano como la regla <c>boolean</c> de Laravel: <c>true</c>, <c>false</c>, 1, 0, "1" y "0".</summary>
    public bool TryBoolean(out bool value)
    {
        switch (Kind)
        {
            case LegacyScalarKind.True:
                value = true;
                return true;
            case LegacyScalarKind.False:
                value = false;
                return true;
            default:
                value = Text == "1";
                return Text is "1" or "0";
        }
    }

    /// <summary>Decimal ya validado; lanza si no lo es (error de programación).</summary>
    public decimal Decimal => TryDecimal(out var value) ? value : throw new InvalidOperationException($"'{Text}' is not a decimal.");

    /// <summary>Entero ya validado; lanza si no lo es.</summary>
    public long Integer => TryInteger(out var value) ? value : throw new InvalidOperationException($"'{Text}' is not an integer.");

    /// <summary>Booleano ya validado; lanza si no lo es.</summary>
    public bool Boolean => TryBoolean(out var value) ? value : throw new InvalidOperationException($"'{Text}' is not a boolean.");

    public override string ToString() => Text;
}

/// <summary>Lee cadenas, números y booleanos JSON como <see cref="LegacyScalar"/>; los objetos y listas no son válidos.</summary>
public sealed class LegacyScalarConverter : JsonConverter<LegacyScalar>
{
    public override LegacyScalar Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.String => new LegacyScalar(reader.GetString() ?? "", LegacyScalarKind.String),
        JsonTokenType.Number => new LegacyScalar(
            reader.HasValueSequence ? Encoding.UTF8.GetString(reader.ValueSequence) : Encoding.UTF8.GetString(reader.ValueSpan),
            LegacyScalarKind.Number),
        JsonTokenType.True => new LegacyScalar("1", LegacyScalarKind.True),
        JsonTokenType.False => new LegacyScalar("", LegacyScalarKind.False),
        _ => throw new JsonException($"Expected a string, a number or a boolean but found {reader.TokenType}."),
    };

    public override void Write(Utf8JsonWriter writer, LegacyScalar value, JsonSerializerOptions options)
    {
        switch (value.Kind)
        {
            case LegacyScalarKind.Number:
                writer.WriteRawValue(value.Text);
                break;
            case LegacyScalarKind.True:
                writer.WriteBooleanValue(true);
                break;
            case LegacyScalarKind.False:
                writer.WriteBooleanValue(false);
                break;
            default:
                writer.WriteStringValue(value.Text);
                break;
        }
    }
}
