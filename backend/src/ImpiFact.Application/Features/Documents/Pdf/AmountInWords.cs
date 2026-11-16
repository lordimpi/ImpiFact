using System.Globalization;

namespace ImpiFact.Application.Features.Documents.Pdf;

/// <summary>
/// Total en letras de la representación gráfica («SON: …»), como <c>NumberSpellOut::convertir</c> del PHP: el entero y
/// los centavos en palabras con las reglas de ICU (la librería de internacionalización que usa el
/// <c>NumberFormatter</c> de PHP) para <c>es-ES</c>, en mayúsculas, seguidos de la moneda.
/// </summary>
/// <remarks>
/// Como ICU, el número final se escribe completo («veintiuno», «ciento uno») y se acorta delante de «mil» y de
/// «millones» («veintiún mil», «un millón»).
/// </remarks>
internal static class AmountInWords
{
    private static readonly string[] Units =
    [
        "cero", "uno", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve", "diez", "once", "doce", "trece",
        "catorce", "quince", "dieciséis", "diecisiete", "dieciocho", "diecinueve", "veinte", "veintiuno", "veintidós",
        "veintitrés", "veinticuatro", "veinticinco", "veintiséis", "veintisiete", "veintiocho", "veintinueve",
    ];

    private static readonly string[] Tens = ["", "", "", "treinta", "cuarenta", "cincuenta", "sesenta", "setenta", "ochenta", "noventa"];

    private static readonly string[] Hundreds =
    [
        "", "ciento", "doscientos", "trescientos", "cuatrocientos", "quinientos", "seiscientos", "setecientos", "ochocientos", "novecientos",
    ];

    /// <summary>
    /// <c>{ENTERO} PESOS CON {CENTAVOS} CENTAVOS</c>, o con el nombre de <paramref name="currencyName"/> en lugar de
    /// «PESOS» cuando el documento trae otra moneda (<c>idcurrency</c>).
    /// </summary>
    public static string Spell(decimal amount, string? currencyName)
    {
        var whole = decimal.Floor(amount);
        var cents = (long)decimal.Round((amount - whole) * 100m, 0, MidpointRounding.AwayFromZero);
        var currency = string.IsNullOrWhiteSpace(currencyName) ? "pesos" : currencyName.Trim();
        return $"{Number((long)whole)} {currency} con {Number(cents)} centavos".ToUpper(CultureInfo.InvariantCulture);
    }

    /// <summary>Número entero no negativo en palabras.</summary>
    public static string Number(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        if (value < 1_000_000)
        {
            return BelowMillion(value);
        }

        var millions = value / 1_000_000;
        var rest = value % 1_000_000;
        var head = millions == 1 ? "un millón" : Shortened(BelowMillion(millions)) + " millones";
        return rest == 0 ? head : head + " " + BelowMillion(rest);
    }

    private static string BelowMillion(long value)
    {
        if (value < 1000)
        {
            return BelowThousand((int)value);
        }

        var thousands = (int)(value / 1000);
        var rest = (int)(value % 1000);
        var head = thousands == 1 ? "mil" : Shortened(BelowThousand(thousands)) + " mil";
        return rest == 0 ? head : head + " " + BelowThousand(rest);
    }

    private static string BelowThousand(int value)
    {
        if (value < 100)
        {
            return BelowHundred(value);
        }

        if (value == 100)
        {
            return "cien";
        }

        var rest = value % 100;
        return rest == 0 ? Hundreds[value / 100] : Hundreds[value / 100] + " " + BelowHundred(rest);
    }

    private static string BelowHundred(int value) =>
        value < 30 ? Units[value] : value % 10 == 0 ? Tens[value / 10] : Tens[value / 10] + " y " + Units[value % 10];

    /// <summary>«uno» se acorta delante de un sustantivo: «veintiún mil», «treinta y un millones».</summary>
    private static string Shortened(string words) =>
        words.EndsWith("veintiuno", StringComparison.Ordinal) ? words[..^"veintiuno".Length] + "veintiún"
        : words.EndsWith("uno", StringComparison.Ordinal) ? words[..^1]
        : words;
}
