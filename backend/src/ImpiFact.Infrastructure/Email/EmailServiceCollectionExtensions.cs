using ImpiFact.Application.Abstractions.Email;
using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ImpiFact.Infrastructure.Email;

public static class EmailServiceCollectionExtensions
{
    /// <summary>
    /// Registra el envío de correo (MailKit), el servidor de correo de cada empresa y el de la plataforma, con las
    /// opciones de la sección «Email» validadas al arrancar. Solo lo llama el Worker, que es quien envía los correos.
    /// </summary>
    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<EmailOptions>(configuration, EmailOptions.SectionName);
        services.TryAddSingleton<IEmailSender, MailKitEmailSender>();
        services.TryAddSingleton<IPlatformMailServer, PlatformMailServer>();
        services.TryAddScoped<ICompanyMailSettingsReads, EfCompanyMailSettingsReads>();
        return services;
    }
}
