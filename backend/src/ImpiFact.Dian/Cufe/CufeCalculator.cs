using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ImpiFact.Dian.Rounding;

namespace ImpiFact.Dian.Cufe;

/// <summary>
/// Ambiente de la DIAN al que va el documento (producción o pruebas). Su valor numérico es el que se
/// escribe en cbc:ProfileExecutionID, el campo del XML que indica el ambiente.
/// </summary>
public enum DianEnvironment
{
    Production = 1,
    Testing = 2,
}

/// <summary>Valores del documento con los que se calcula el CUFE o el CUDE (ver <see cref="CufeCalculator"/>).</summary>
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
/// Calcula el CUFE (código único de la factura electrónica) y el CUDE (código único de documento
/// electrónico, para notas crédito y débito): un hash que identifica el documento ante la DIAN.
/// </summary>
/// <remarks>
/// Es el SHA-384, en hexadecimal en minúsculas, de la concatenación de los valores exactamente como se
/// escriben en el XML (anexo técnico de la DIAN). Confirmado contra los casos de referencia (golden): XML y CUFE
/// generados por ApiDian, el sistema PHP original.
/// </remarks>
public static class CufeCalculator
{
    /// <summary>
    /// Desfase horario de Colombia respecto a UTC. Colombia no tiene horario de verano, así que el XML
    /// siempre lleva -05:00.
    /// </summary>
    public const string ColombiaOffset = "-05:00";

    private const string IvaCode = "01";
    private const string IncCode = "04";
    private const string IcaCode = "03";

    /// <summary>CUFE de una factura; usa la clave técnica de la resolución de numeración.</summary>
    public static string ComputeCufe(CufeInput input, string technicalKey) => Compute(input, technicalKey);

    /// <summary>CUDE de una nota crédito o débito; usa el PIN del software en lugar de la clave técnica.</summary>
    public static string ComputeCude(CufeInput input, string softwarePin) => Compute(input, softwarePin);

    private static string Compute(CufeInput input, string secret)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrEmpty(secret);

        // El orden lo fija la DIAN. Cada impuesto va como código + valor (01 IVA, 04 INC, 03 ICA), con
        // 0.00 cuando el documento no lo tiene.
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
