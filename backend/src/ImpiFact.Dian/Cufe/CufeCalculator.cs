using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ImpiFact.Dian.Rounding;

namespace ImpiFact.Dian.Cufe;

/// <summary>Ambiente DIAN, tal como se escribe en cbc:ProfileExecutionID.</summary>
public enum DianEnvironment
{
    Production = 1,
    Testing = 2,
}

/// <summary>Valores que entran en el CUFE/CUDE, tomados de los totales del documento.</summary>
public sealed record CufeInput(
    string Number,
    DateOnly IssueDate,
    TimeOnly IssueTime,
    decimal LineExtensionAmount,
    decimal? Iva,
    decimal? Inc,
    decimal? Ica,
    decimal PayableAmount,
    string SupplierNit,
    string CustomerNit,
    DianEnvironment Environment);

/// <summary>
/// CUFE (facturas) y CUDE (notas): SHA-384, hexadecimal en minúsculas, sobre la concatenación de los
/// valores exactamente como se escriben en el XML (anexo técnico de la DIAN; confirmado contra las
/// fixtures golden de ApiDian).
/// </summary>
public static class CufeCalculator
{
    /// <summary>Colombia no tiene horario de verano: el XML siempre lleva -05:00.</summary>
    public const string ColombiaOffset = "-05:00";

    private const string IvaCode = "01";
    private const string IncCode = "04";
    private const string IcaCode = "03";

    public static string ComputeCufe(CufeInput input, string technicalKey) => Compute(input, technicalKey);

    public static string ComputeCude(CufeInput input, string softwarePin) => Compute(input, softwarePin);

    private static string Compute(CufeInput input, string secret)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrEmpty(secret);

        var text = string.Concat(
            input.Number,
            input.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            input.IssueTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + ColombiaOffset,
            DianRounding.FormatAmount(input.LineExtensionAmount),
            IvaCode, DianRounding.FormatAmount(input.Iva ?? 0m),
            IncCode, DianRounding.FormatAmount(input.Inc ?? 0m),
            IcaCode, DianRounding.FormatAmount(input.Ica ?? 0m),
            DianRounding.FormatAmount(input.PayableAmount),
            input.SupplierNit,
            input.CustomerNit,
            secret,
            ((int)input.Environment).ToString(CultureInfo.InvariantCulture));

        return Convert.ToHexStringLower(SHA384.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
