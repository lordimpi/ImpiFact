using System.Text.RegularExpressions;

namespace ImpiFact.Contract.Tests.Postman;

/// <summary>
/// Lo que se espera de cada solicitud: un código HTTP de éxito (2xx), como el PHP, y las claves del primer nivel de la
/// respuesta de ejemplo que guarda la colección. Pocas solicitudes traen ejemplo, así que la respuesta de ejemplo de
/// una ruta vale para todas las solicitudes de esa misma ruta (por ejemplo, las dos facturas con ejemplo fijan las
/// claves de todas las facturas) y, para unas pocas rutas sin ejemplo propio, se toma el de la ruta hermana que el PHP
/// responde con la misma forma (<see cref="Siblings"/>). Sin ejemplo solo se revisa el código HTTP.
/// </summary>
public sealed partial class ExpectedContract
{
    /// <summary>
    /// Ruta sin ejemplo → ruta cuyo ejemplo tiene la misma forma en el PHP (inventario de reglas, «php-rules»).
    /// </summary>
    private static readonly Dictionary<string, string> Siblings = new(StringComparer.Ordinal)
    {
        // Sección 4.4: la nota débito responde como la nota crédito (sin success, con cude).
        ["POST /api/ubl2.1/debit-note"] = "POST /api/ubl2.1/credit-note",
        // Sections 9.2 (item 3) and 10: the AIU, mandate and transport invoices answer like the invoice in /api/v1 (Mejora:
        // the PHP drops success and whatsapp_response, which the invoice example has and ImpiFact keeps).
        ["POST /api/ubl2.1/invoice-aiu"] = "POST /api/ubl2.1/invoice",
        ["POST /api/ubl2.1/invoice-mandate"] = "POST /api/ubl2.1/invoice",
        ["POST /api/ubl2.1/invoice-transport"] = "POST /api/ubl2.1/invoice",
        // Sección 5.4.1: StateController arma la misma respuesta para status/document y status/zip.
        ["POST /api/ubl2.1/status/document/{}"] = "POST /api/ubl2.1/status/zip/{}",
        // ConfigurationController responde igual al guardar el software de cada tipo de documento.
        ["PUT /api/ubl2.1/config/software"] = "PUT /api/ubl2.1/config/softwarepayroll",
    };

    private readonly Dictionary<string, IReadOnlyList<string>> keysByRoute;

    public ExpectedContract(PostmanCollection collection)
    {
        keysByRoute = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var request in collection.Requests)
        {
            var keys = SuccessKeys(request);
            if (keys is not null)
            {
                keysByRoute.TryAdd(RouteOf(request), keys);
            }
        }
    }

    /// <summary>Claves del primer nivel que la respuesta debe traer; vacío si la colección no da cómo saberlo.</summary>
    public IReadOnlyList<string> KeysFor(PostmanRequest request)
    {
        var route = RouteOf(request);
        return SuccessKeys(request)
            ?? (keysByRoute.TryGetValue(route, out var keys) ? keys : null)
            ?? (Siblings.TryGetValue(route, out var sibling) && keysByRoute.TryGetValue(sibling, out var siblingKeys) ? siblingKeys : null)
            ?? [];
    }

    /// <summary>
    /// Método y ruta con los segmentos variables (números, NIT, CUFE, UUID, nombres de archivo) como <c>{}</c>, por
    /// ejemplo <c>POST /api/ubl2.1/status/zip/{}</c>.
    /// </summary>
    public static string RouteOf(PostmanRequest request)
    {
        var segments = request.ApiPath.Split('/').Select(segment => VariableSegment().IsMatch(segment) ? "{}" : segment);
        return $"{request.Method} {string.Join('/', segments)}";
    }

    private static IReadOnlyList<string>? SuccessKeys(PostmanRequest request) =>
        request.Examples.Where(example => example.Code is >= 200 and < 300).Select(example => example.TopLevelKeys).FirstOrDefault(keys => keys is not null);

    [GeneratedRegex(@"^(\d+|[0-9a-fA-F]{32,}|[0-9a-fA-F-]{36}|.+\.(xml|pdf|zip)|\d{4}-\d{2}-\d{2})$")]
    private static partial Regex VariableSegment();
}
