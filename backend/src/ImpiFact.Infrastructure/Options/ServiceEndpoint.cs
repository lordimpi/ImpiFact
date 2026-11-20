using System.Text.RegularExpressions;

namespace ImpiFact.Infrastructure.Options;

/// <summary>
/// Reglas comunes para las direcciones y regiones de los servicios de nube que llegan por configuración (almacenamiento y
/// secretos). Las comparten los validadores de <see cref="StorageOptions"/> y <see cref="SecretsOptions"/>.
/// </summary>
internal static partial class ServiceEndpoint
{
    /// <summary>
    /// Una URL absoluta <c>http</c> o <c>https</c> sin usuario, contraseña, consulta ni fragmento: las credenciales (una
    /// llave, una SAS) nunca viajan en la configuración, siempre salen de la identidad de la carga de trabajo.
    /// </summary>
    public static bool IsValidUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var url)
        && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)
        && url.UserInfo.Length == 0
        && url.Query.Length == 0
        && url.Fragment.Length == 0;

    /// <summary>Si es una URL absoluta <c>https</c> con las mismas condiciones de <see cref="IsValidUrl"/>.</summary>
    public static bool IsValidHttpsUrl(string value) =>
        IsValidUrl(value) && new Uri(value).Scheme == Uri.UriSchemeHttps;

    /// <summary>Forma de una región de AWS: «us-east-1», «sa-east-1», «us-gov-west-1».</summary>
    public static bool IsAwsRegion(string value) => AwsRegion().IsMatch(value);

    /// <summary>
    /// Mensaje de falla que nombra el dato de configuración sin repetir su valor cuando podría llevar una credencial
    /// (usuario, contraseña o consulta).
    /// </summary>
    public static string Describe(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var url) && (url.UserInfo.Length > 0 || url.Query.Length > 0)
            ? "(value hidden: it carries credentials or a query)"
            : $"'{value}'";

    [GeneratedRegex(@"^[a-z]{2}(?:-[a-z]+)+-[0-9]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex AwsRegion();
}
