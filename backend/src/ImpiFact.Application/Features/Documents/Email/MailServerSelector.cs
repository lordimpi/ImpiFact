using ImpiFact.Application.Abstractions.Email;
using ImpiFact.Application.Abstractions.Protection;

namespace ImpiFact.Application.Features.Documents.Email;

/// <summary>
/// Elige el servidor de correo (SMTP, el protocolo de envío de correo) de una empresa, como <c>applyMailConfig</c> y
/// <c>validate_mail_server</c> de ApiDian: el de la empresa si lo tiene completo (servidor, puerto, usuario,
/// contraseña y cifrado) y no eligió el de la plataforma; si no, el de la plataforma. El remitente es el de la empresa
/// si lo tiene y, si no, el de la plataforma.
/// </summary>
/// <remarks>
/// La contraseña de la empresa se descifra aquí, justo antes de enviar, y solo vive en memoria. Diferencia con el
/// PHP: los datos del servidor que trae la solicitud (<c>smtp_parameters</c>) no se usan, porque no se guardan.
/// </remarks>
internal sealed class MailServerSelector(ICompanyMailSettingsReads settings, IPlatformMailServer platform, ISecretProtector secrets)
{
    public async Task<MailServer> SelectAsync(Guid companyId, CancellationToken ct)
    {
        var company = await settings.GetAsync(companyId, ct);
        if (company is not { SendsThroughCompany: true, Port: { } port })
        {
            return platform.Default;
        }

        var fallback = platform.Default;
        return new MailServer(
            company.Host!.Trim(),
            port,
            company.Username!.Trim(),
            secrets.Unprotect(SecretPurposes.SmtpPassword, company.PasswordCipher!),
            EncryptionOf(company.Encryption!),
            string.IsNullOrWhiteSpace(company.FromAddress) ? fallback.FromAddress : company.FromAddress.Trim(),
            string.IsNullOrWhiteSpace(company.FromName) ? fallback.FromName : company.FromName.Trim());
    }


    /// <summary>
    /// Cifrado como lo guarda el PHP: <c>ssl</c> es TLS desde el inicio, <c>tls</c> (o <c>starttls</c>) es STARTTLS,
    /// <c>none</c> es sin cifrar; cualquier otro valor lo decide la librería.
    /// </summary>
    private static MailEncryption EncryptionOf(string encryption) => encryption.Trim().ToLowerInvariant() switch
    {
        "ssl" => MailEncryption.SslOnConnect,
        "tls" or "starttls" => MailEncryption.StartTls,
        "none" => MailEncryption.None,
        _ => MailEncryption.Auto,
    };
}
