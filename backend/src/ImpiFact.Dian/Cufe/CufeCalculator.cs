using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ImpiFact.Dian.Rounding;

namespace ImpiFact.Dian.Cufe;

/// <summary>DIAN environment, as written in cbc:ProfileExecutionID.</summary>
public enum DianEnvironment
{
    Production = 1,
    Testing = 2,
}

/// <summary>Values that enter the CUFE/CUDE, taken from the document totals.</summary>
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
/// CUFE (invoices) and CUDE (notes): SHA-384, lowercase hex, over the concatenation of the
/// values exactly as they are written in the XML (DIAN technical annex; confirmed against the
/// ApiDian golden fixtures).
/// </summary>
public static class CufeCalculator
{
    /// <summary>Colombia has no daylight saving time: the XML always carries -05:00.</summary>
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
