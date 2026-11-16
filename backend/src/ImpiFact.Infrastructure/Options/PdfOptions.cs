using System.ComponentModel.DataAnnotations;

namespace ImpiFact.Infrastructure.Options;

/// <summary>
/// Generación del PDF (sección «Pdf»; Arquitectura detallada, sección 11 y ADR-04): el HTML de la plantilla se convierte
/// en PDF con Gotenberg, un contenedor aparte con Chromium (el navegador) que expone una API HTTP. No hay secretos: a
/// Gotenberg solo se le manda el HTML del documento. Los valores por defecto sirven para el compose de desarrollo.
/// </summary>
public sealed class PdfOptions : IValidatableObject
{
    public const string SectionName = "Pdf";

    /// <summary>Dirección base de Gotenberg, por ejemplo <c>http://gotenberg:3000</c> dentro de la red de contenedores.</summary>
    [Required]
    public Uri? GotenbergUrl { get; set; } = new("http://127.0.0.1:3000");

    /// <summary>Segundos que puede tardar una conversión antes de cancelarla; el mensaje se reintenta.</summary>
    [Range(1, 240)]
    public int TimeoutSeconds { get; set; } = 60;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (GotenbergUrl is not { IsAbsoluteUri: true } url || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            yield return new ValidationResult($"{nameof(GotenbergUrl)} must be an absolute http or https URL.", [nameof(GotenbergUrl)]);
        }
    }
}
