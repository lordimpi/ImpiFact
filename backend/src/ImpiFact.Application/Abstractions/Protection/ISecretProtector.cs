namespace ImpiFact.Application.Abstractions.Protection;

/// <summary>
/// Cifra y descifra secretos cortos que se guardan en columnas <c>*_cipher</c> de la base de datos: el PIN del
/// software, la contraseña SMTP o un token (Modelo de datos detallado, sección 3.3). El texto cifrado nunca se
/// registra en logs ni en <c>audit_logs</c>.
/// </summary>
/// <remarks>
/// El propósito (<c>purpose</c> en cada método) separa los usos: lo que se cifró con un propósito solo se
/// descifra con ese mismo propósito. Así un PIN cifrado no se puede leer pasándolo como contraseña SMTP. Use las
/// constantes de <see cref="SecretPurposes"/>.
/// </remarks>
public interface ISecretProtector
{
    /// <summary>Cifra <paramref name="plain"/> y devuelve un texto apto para guardar en una columna.</summary>
    string Protect(string purpose, string plain);

    /// <summary>
    /// Descifra un texto que produjo <see cref="Protect"/> con el mismo propósito. Si el texto fue alterado, se
    /// cifró con otro propósito o con llaves que ya no existen, lanza
    /// <see cref="System.Security.Cryptography.CryptographicException"/>: es un error inesperado, no un flujo previsto.
    /// </summary>
    string Unprotect(string purpose, string cipher);
}

/// <summary>Propósitos de cifrado de los secretos que guarda la aplicación. No se cambian una vez en uso.</summary>
public static class SecretPurposes
{
    /// <summary>PIN del software de facturación ante la DIAN (<c>company_software.pin_cipher</c>).</summary>
    public const string SoftwarePin = "company-software.pin";

    /// <summary>Contraseña del servidor de correo de la empresa (<c>company_mail_settings.password_cipher</c>).</summary>
    public const string SmtpPassword = "company-mail.password";
}
