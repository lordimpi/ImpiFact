using System.Security.Cryptography;
using System.Text;

namespace ImpiFact.Dian.Ubl;

/// <summary>
/// sts:SoftwareSecurityCode: SHA-384, lowercase hex, of software ID + software PIN + document
/// number (cbc:ID), without separators («Reglas del PHP», section 2.1).
/// </summary>
public static class SoftwareSecurityCode
{
    public static string Compute(string softwareId, string pin, string documentNumber)
    {
        ArgumentException.ThrowIfNullOrEmpty(softwareId);
        ArgumentException.ThrowIfNullOrEmpty(pin);
        ArgumentException.ThrowIfNullOrEmpty(documentNumber);
        return Convert.ToHexStringLower(SHA384.HashData(Encoding.UTF8.GetBytes(softwareId + pin + documentNumber)));
    }
}
