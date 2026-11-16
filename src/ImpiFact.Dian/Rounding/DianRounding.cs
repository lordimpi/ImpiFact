using System.Globalization;

namespace ImpiFact.Dian.Rounding;

/// <summary>
/// Single place for DIAN rounding and number formatting. Matches ApiDian, whose templates use
/// PHP's number_format: half away from zero, 2 decimals for amounts and prices, 6 for quantities
/// and 3 for withholdings. Values are always <see cref="decimal"/>: a double turns 6.965 into
/// 6.96499... and rounds it the wrong way.
/// </summary>
public static class DianRounding
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Parses a number written with a dot as decimal separator, regardless of culture.</summary>
    public static decimal Parse(string text) =>
        decimal.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Invariant);

    public static decimal RoundAmount(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal RoundQuantity(decimal value) => Math.Round(value, 6, MidpointRounding.AwayFromZero);

    public static decimal RoundWithholding(decimal value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);

    public static string FormatAmount(decimal value) => RoundAmount(value).ToString("0.00", Invariant);

    public static string FormatQuantity(decimal value) => RoundQuantity(value).ToString("0.000000", Invariant);

    public static string FormatWithholding(decimal value) => RoundWithholding(value).ToString("0.000", Invariant);
}
