using System.ComponentModel.DataAnnotations;
using System.Net;

namespace ImpiFact.Infrastructure.Options;

/// <summary>
/// Panel web (sección «Panel»): su dirección pública, con la que el Worker arma los enlaces de los correos (por ejemplo,
/// el de restablecer la contraseña). Solo la usa el Worker, que la valida al arrancar.
/// </summary>
public sealed class PanelOptions : IValidatableObject
{
    public const string SectionName = "Panel";

    /// <summary>
    /// Dirección absoluta del panel, por ejemplo <c>https://panel.impifact.co</c> o con una ruta base
    /// (<c>https://impifact.co/panel</c>). Debe ser HTTPS; HTTP solo hacia <c>localhost</c> o una IP de loopback (la
    /// propia máquina), para desarrollo. Sin consulta (<c>?</c>) ni fragmento (<c>#</c>).
    /// </summary>
    [Required]
    public string BaseUrl { get; set; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            yield break;
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && IsLoopback(uri)))
            || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
        {
            yield return new ValidationResult(
                $"{SectionName}:{nameof(BaseUrl)} must be an absolute https URL (http only for localhost or a loopback IP), without query or fragment.",
                [nameof(BaseUrl)]);
        }
    }

    private static bool IsLoopback(Uri uri) =>
        uri.IsLoopback || (IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address));
}
