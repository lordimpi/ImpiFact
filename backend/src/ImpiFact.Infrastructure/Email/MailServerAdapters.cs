using ImpiFact.Application.Abstractions.Email;
using ImpiFact.Domain.Companies;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.Email;

/// <summary>
/// Servidor de correo de la empresa (<c>company_mail_settings</c>) con EF Core (Entity Framework, el mapeador de
/// objetos a tablas), sin rastreo. Quita solo el filtro de tenant (cada empresa cliente con sus datos aislados) y pone
/// la empresa en el WHERE, como las demás lecturas. La contraseña sale cifrada, tal como está guardada.
/// </summary>
internal sealed class EfCompanyMailSettingsReads(ImpiFactDbContext db) : ICompanyMailSettingsReads
{
    public Task<CompanyMailServerSettings?> GetAsync(Guid companyId, CancellationToken ct) =>
        db.Set<CompanyMailSettings>()
            .AsNoTracking()
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(settings => settings.CompanyId == companyId)
            .Select(settings => new CompanyMailServerSettings(
                settings.UsePlatformDefault,
                settings.Host,
                settings.Port,
                settings.Username,
                settings.PasswordCipher,
                settings.Encryption,
                settings.FromAddress,
                settings.FromName))
            .FirstOrDefaultAsync(ct);
}

/// <summary>Servidor de correo por defecto de la plataforma, de la sección «Email» ya validada.</summary>
internal sealed class PlatformMailServer(IOptions<EmailOptions> options) : IPlatformMailServer
{
    public MailServer Default
    {
        get
        {
            var email = options.Value;
            return new MailServer(
                email.Host.Trim(),
                email.Port,
                string.IsNullOrWhiteSpace(email.Username) ? null : email.Username.Trim(),
                string.IsNullOrEmpty(email.Password) ? null : email.Password,
                email.Encryption.ToLowerInvariant() switch
                {
                    EmailOptions.NoEncryption => MailEncryption.None,
                    EmailOptions.SslOnConnect => MailEncryption.SslOnConnect,
                    EmailOptions.StartTls => MailEncryption.StartTls,
                    _ => MailEncryption.Auto,
                },
                email.FromAddress.Trim(),
                string.IsNullOrWhiteSpace(email.FromName) ? null : email.FromName.Trim());
        }
    }
}
