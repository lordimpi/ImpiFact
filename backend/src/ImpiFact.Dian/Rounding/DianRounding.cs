using System.Globalization;

namespace ImpiFact.Dian.Rounding;

/// <summary>
/// Único lugar para el redondeo y el formato de números de la DIAN. Coincide con ApiDian, cuyas
/// plantillas usan number_format de PHP: mitad alejándose de cero, 2 decimales para valores y precios,
/// 6 para cantidades y 3 para retenciones. Los valores son siempre <see cref="decimal"/>: un double
/// convierte 6.965 en 6.96499... y lo redondea en el sentido equivocado.
/// </summary>
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
