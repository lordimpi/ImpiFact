using ImpiFact.Application.Abstractions.Protection;
using Microsoft.AspNetCore.DataProtection;

namespace ImpiFact.Infrastructure.Protection;

/// <summary>
/// <see cref="ISecretProtector"/> sobre ASP.NET Core Data Protection: el sistema de .NET que cifra datos con
/// llaves que él mismo crea, rota cada 90 días y conserva para seguir descifrando lo antiguo (Arquitectura
/// detallada, sección 19.2). Las llaves viven en la tabla <c>data_protection_keys</c>, compartida por todas las
/// instancias, y quedan cifradas con la llave maestra (ver <see cref="LocalAesGcmXmlEncryptor"/>).
/// </summary>
internal sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    /// <summary>Propósito raíz; cada secreto agrega el suyo debajo. No se cambia: dejaría ilegibles los datos.</summary>
    private const string RootPurpose = "ImpiFact.Secrets";

    public string Protect(string purpose, string plain)
    {
        ArgumentNullException.ThrowIfNull(plain);
        return Protector(purpose).Protect(plain);
    }

    public string Unprotect(string purpose, string cipher)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        return Protector(purpose).Unprotect(cipher);
    }

    private IDataProtector Protector(string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        return provider.CreateProtector(RootPurpose, purpose);
    }
}
