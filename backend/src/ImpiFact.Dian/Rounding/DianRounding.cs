using System.Globalization;

namespace ImpiFact.Dian.Rounding;

/// <summary>
/// Único lugar donde se redondean y formatean los números que van en los documentos de la DIAN.
/// </summary>
/// <remarks>
/// Imita a ApiDian (la implementación de referencia en PHP), cuyas plantillas usan number_format de PHP:
/// un valor que queda justo en la mitad se redondea alejándose de cero (2.345 da 2.35 y -2.345 da -2.35).
/// Se usan 2 decimales para valores y precios, 6 para cantidades y 3 para retenciones. Los valores son
/// siempre <see cref="decimal"/>, nunca double: un double guarda 6.965 como 6.96499... y lo redondearía
/// hacia el lado equivocado.
/// </remarks>
public static class DianRounding
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Interpreta un número escrito con punto como separador decimal, sin importar la cultura.</summary>
    public static decimal Parse(string text) =>
        decimal.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Invariant);

    public static decimal RoundAmount(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal RoundQuantity(decimal value) => Math.Round(value, 6, MidpointRounding.AwayFromZero);

    public static decimal RoundWithholding(decimal value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);

    public static string FormatAmount(decimal value) => RoundAmount(value).ToString("0.00", Invariant);

    public static string FormatQuantity(decimal value) => RoundQuantity(value).ToString("0.000000", Invariant);

    public static string FormatWithholding(decimal value) => RoundWithholding(value).ToString("0.000", Invariant);
}
