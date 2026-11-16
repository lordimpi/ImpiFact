using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Infrastructure.Protection;

/// <summary>
/// Abre un PKCS#12 (el archivo .p12 o .pfx con el certificado y su llave privada) en memoria y revisa lo que la
/// firma de la DIAN necesita: la contraseña correcta, una llave privada RSA y que el certificado no esté vencido.
/// Nada se escribe a disco: la llave se abre con <see cref="X509KeyStorageFlags.EphemeralKeySet"/>.
/// </summary>
internal sealed class CertificateInspector(IClock clock) : ICertificateInspector
{
    /// <summary>Colombia está en UTC-5 todo el año, sin horario de verano.</summary>
    private static readonly TimeSpan ColombiaOffset = TimeSpan.FromHours(-5);

    public Result<CertificateDetails> Inspect(byte[] pfx, string password)
    {
        ArgumentNullException.ThrowIfNull(pfx);

        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadPkcs12(pfx, password, X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (CryptographicException)
        {
            // La plataforma no distingue una contraseña equivocada de un archivo dañado: ambos son el mismo error.
            return CertificateErrors.InvalidPasswordOrFormat;
        }

        using (certificate)
        {
            using var privateKey = certificate.GetRSAPrivateKey();
            if (privateKey is null)
            {
                return CertificateErrors.MissingRsaPrivateKey;
            }

            var notBefore = new DateTimeOffset(certificate.NotBefore).ToUniversalTime();
            var notAfter = new DateTimeOffset(certificate.NotAfter).ToUniversalTime();
            var daysLeft = DaysLeft(notAfter, clock.UtcNow);
            if (daysLeft <= 0)
            {
                return CertificateErrors.Expired;
            }

            return new CertificateDetails(
                certificate.GetCertHashString(HashAlgorithmName.SHA256),
                certificate.Subject,
                certificate.Issuer,
                certificate.SerialNumber,
                notBefore,
                notAfter,
                daysLeft);
        }
    }

    /// <summary>
    /// Regla del PHP (<c>verify_certificate</c>): días entre la fecha de hoy y la de vencimiento, ambas en la
    /// hora de Colombia y sin la hora del día. Cero o menos significa vencido.
    /// </summary>
    private static int DaysLeft(DateTimeOffset notAfter, DateTimeOffset now) =>
        DateOnly.FromDateTime(notAfter.ToOffset(ColombiaOffset).DateTime).DayNumber
        - DateOnly.FromDateTime(now.ToOffset(ColombiaOffset).DateTime).DayNumber;
}
