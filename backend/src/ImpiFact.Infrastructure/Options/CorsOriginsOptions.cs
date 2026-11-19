using System.ComponentModel.DataAnnotations;

namespace ImpiFact.Infrastructure.Options;

/// <summary>
/// CORS (Cross-Origin Resource Sharing: permiso para que una página de otro dominio, como el panel, llame a la
/// API desde el navegador). Solo se aceptan los orígenes de esta lista explícita; si está vacía, ninguno
/// (Arquitectura detallada, sección 9.1). Estos orígenes pueden enviar credenciales (la cookie de sesión del panel),
/// por eso nunca se acepta el comodín <c>*</c>.
/// </summary>
public sealed class CorsOriginsOptions : IValidatableObject
{
    public const string SectionName = "Cors";

    /// <summary>
    /// Orígenes permitidos, tal como los envía el navegador en el encabezado <c>Origin</c>: esquema, host y puerto si no
    /// es el de siempre, sin ruta ni barra final. Por ejemplo <c>https://app.example.com</c>.
    /// </summary>
    public string[] Origins { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        for (var index = 0; index < Origins.Length; index++)
        {
            if (!IsOrigin(Origins[index]))
            {
                yield return new ValidationResult(
                    $"{SectionName}:{nameof(Origins)}:{index} must be an explicit origin such as https://app.example.com " +
                    "(scheme, host and optional port; no wildcard, path or trailing slash).",
                    [nameof(Origins)]);
            }
        }
    }

    private static bool IsOrigin(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "https" or "http"
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.Equals(value, uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
}
