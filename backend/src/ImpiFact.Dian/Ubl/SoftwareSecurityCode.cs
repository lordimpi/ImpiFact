using System.Security.Cryptography;
using System.Text;

namespace ImpiFact.Dian.Ubl;

/// <summary>
/// Calcula el código de seguridad del software (sts:SoftwareSecurityCode), que liga el documento al
/// software registrado ante la DIAN: solo quien conoce el PIN del software puede calcularlo.
/// </summary>
/// <remarks>
/// Es el SHA-384, en hexadecimal en minúsculas, de ID del software + PIN del software + número del
/// documento (cbc:ID), sin separadores («Reglas del PHP», sección 2.1).
/// </remarks>
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
