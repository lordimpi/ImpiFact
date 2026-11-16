using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Abstractions.Protection;

/// <summary>
/// Revisa un certificado PKCS#12 (el archivo .pfx o .p12 que reúne el certificado y su llave privada) antes de
/// guardarlo: que abra con su contraseña, que traiga una llave privada RSA y que no esté vencido.
/// </summary>
public interface ICertificateInspector
{
    /// <summary>
    /// Abre el certificado en memoria y devuelve sus datos, o un error de validación si la contraseña no
    /// corresponde, si no trae llave privada RSA o si ya venció.
    /// </summary>
    Result<CertificateDetails> Inspect(byte[] pfx, string password);
}

/// <summary>Datos públicos de un certificado; ninguno es secreto.</summary>
/// <param name="Thumbprint">Huella SHA-256 del certificado en hexadecimal (64 caracteres, mayúsculas).</param>
/// <param name="Subject">Titular, como nombre distinguido (por ejemplo <c>CN=Empresa, C=CO</c>).</param>
/// <param name="Issuer">Entidad que lo emitió, en el mismo formato.</param>
/// <param name="SerialNumber">Número de serie en hexadecimal.</param>
/// <param name="NotBefore">Inicio de la vigencia.</param>
/// <param name="NotAfter">Fin de la vigencia.</param>
/// <param name="DaysLeft">
/// Días que faltan para el vencimiento, contados como el PHP (<c>certificate_days_left</c>): días entre la fecha
/// de hoy y la fecha de vencimiento, ambas en la hora de Colombia y sin la hora del día.
/// </param>
public sealed record CertificateDetails(
    string Thumbprint,
    string Subject,
    string Issuer,
    string SerialNumber,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    int DaysLeft);

/// <summary>Errores esperados al revisar un certificado.</summary>
public static class CertificateErrors
{
    public static readonly Error InvalidPasswordOrFormat = new(
        "certificate.invalid",
        "No se pudo abrir el certificado: la contraseña no es correcta o el archivo no es un PKCS#12 (.p12 o .pfx) válido.",
        ErrorType.Validation);

    public static readonly Error MissingRsaPrivateKey = new(
        "certificate.no_private_key",
        "El certificado no trae una llave privada RSA; sin ella no se puede firmar.",
        ErrorType.Validation);

    public static readonly Error Expired = new(
        "certificate.expired",
        "El certificado digital ya se encuentra vencido.",
        ErrorType.Validation);
}
