using ImpiFact.Application.Abstractions.Pdf;
using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.Pdf;

public static class PdfServiceCollectionExtensions
{
    /// <summary>
    /// Registra la generación del PDF (<see cref="IPdfRenderer"/>) para el Worker: la de <see cref="AddPdfRendering"/> y,
    /// además, la regla de que el plazo de una conversión quepa en el de un handler.
    /// </summary>
    /// <remarks>
    /// El plazo de una conversión debe caber en el de un handler (<c>Outbox:HandlerTimeoutSeconds</c>): si no, el
    /// Worker cancelaría el handler antes de que Gotenberg termine.
    /// </remarks>
    public static IServiceCollection AddPdf(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPdfRendering(configuration);
        services.AddOptions<PdfOptions>()
            .Validate<IOptions<OutboxOptions>>(
                (pdf, outbox) => pdf.TimeoutSeconds < outbox.Value.HandlerTimeoutSeconds,
                $"{PdfOptions.SectionName}:{nameof(PdfOptions.TimeoutSeconds)} must be shorter than " +
                $"{OutboxOptions.SectionName}:{nameof(OutboxOptions.HandlerTimeoutSeconds)}.");
        return services;
    }

    /// <summary>
    /// Registra la generación del PDF (<see cref="IPdfRenderer"/>): las plantillas HTML, ya compiladas, y el cliente de
    /// Gotenberg con su dirección y plazo de la sección «Pdf», validados al arrancar. La usan el Worker (con
    /// <see cref="AddPdf"/>) y la API, para la vista previa de la factura antes de emitirla.
    /// </summary>
    public static IServiceCollection AddPdfRendering(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<PdfOptions>(configuration, PdfOptions.SectionName);
        services.TryAddSingleton<DocumentHtmlTemplates>();
        services.AddHttpClient<GotenbergClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<PdfOptions>>().Value;
            client.BaseAddress = new Uri(options.GotenbergUrl!.AbsoluteUri.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });
        services.TryAddScoped<IPdfRenderer, GotenbergPdfRenderer>();
        return services;
    }
}
