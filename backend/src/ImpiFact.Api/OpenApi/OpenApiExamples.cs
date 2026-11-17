using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace ImpiFact.Api.OpenApi;

/// <summary>Un ejemplo de cuerpo de solicitud que Scalar muestra precargado y que se elige por su resumen.</summary>
/// <param name="Name">Clave del ejemplo en el documento OpenAPI (la descripción estándar de la API).</param>
/// <param name="Summary">Resumen corto en español; es lo que Scalar muestra en el selector de ejemplos.</param>
/// <param name="Value">El cuerpo JSON tal como se envía.</param>
/// <param name="Description">Aclaración opcional, por ejemplo qué valor reemplazar antes de enviar.</param>
internal sealed record RequestExample(string Name, string Summary, JsonNode Value, string? Description = null);

/// <summary>
/// Ejemplos listos para enviar desde Scalar (el visor interactivo de la API en <c>/scalar</c>): cuerpos de solicitud
/// con varios ejemplos con nombre y valores de ejemplo para los parámetros de la ruta y de la consulta. Los cuerpos de
/// emisión son los casos golden (<c>tests/golden/.../input.json</c>, solicitudes que la DIAN, la Dirección de Impuestos y
/// Aduanas Nacionales, aceptó), incrustados al compilar en el ensamblado; los de configuración están en
/// <see cref="ExampleBodies"/>. Solo se toca el documento OpenAPI: ninguna ruta cambia su comportamiento.
/// </summary>
internal static class OpenApiExamples
{
    /// <summary>NIT (número de identificación tributaria) de ejemplo; su dígito de verificación es 8.</summary>
    public const string Nit = "900123456";

    public const string Dv = "8";

    /// <summary>Identificador de ejemplo de un documento o de una API key.</summary>
    private const string Id = "0196f3a2-7c41-7d2e-9b8a-3c4d5e6f7a8b";

    /// <summary>Identificador de ejemplo del set de pruebas que entrega la DIAN al registrar el software.</summary>
    public const string TestSetId = "2b9c6d1e-4f3a-4b8c-9d7e-1a2b3c4d5e6f";

    /// <summary>Identificador de ejemplo de un envío del set de pruebas (ZipKey).</summary>
    private const string ZipKey = "7d1e2f3a-8b9c-4d0e-a1b2-c3d4e5f6a7b8";

    /// <summary>CUFE (código único de la factura) con la forma que exige la DIAN: SHA-384 en hexadecimal.</summary>
    private const string Cufe =
        "0ca39906d8c33e7b2b965be29afcd74fc6746835362bce580f26d49868bc80a5445bdb6c57bbc164484c112df5117cb3";

    public static IReadOnlyList<RequestExample> Invoices { get; } =
    [
        Golden("invoice/single-line-iva19", "Factura simple: una línea con IVA del 19 %"),
        Golden("invoice/two-lines-discounts-iva19", "Factura con descuentos: dos líneas con IVA del 19 %"),
        Golden("invoice/mixed-iva-19-5-0", "Factura con IVA mixto: líneas al 19 %, al 5 % y exentas (0 %)"),
        Golden("invoice/final-consumer", "Factura a consumidor final (222222222222)"),
    ];

    public static IReadOnlyList<RequestExample> CreditNotes { get; } =
    [
        Golden("credit-note/partial-return-with-reference", "Devolución parcial de la factura SETP990000001",
            "Referencia por su CUFE la factura del ejemplo «con descuentos» (SETP990000001), que debe estar aceptada por la DIAN."),
        Golden("credit-note/without-reference-invoice-period", "Rebaja sin factura de referencia, por periodo (`invoice_period`)"),
    ];

    public static IReadOnlyList<RequestExample> DebitNotes { get; } =
    [
        Golden("debit-note/price-adjustment-with-reference", "Ajuste de precio de la factura SETP990000001",
            "Referencia por su CUFE la factura del ejemplo «con descuentos» (SETP990000001), que debe estar aceptada por la DIAN."),
    ];

    /// <summary>Agrega los ejemplos al cuerpo JSON que el documento ya describe para esta ruta.</summary>
    public static TBuilder WithRequestExamples<TBuilder>(this TBuilder builder, params RequestExample[] examples)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddOpenApiOperationTransformer((operation, _, _) =>
        {
            if (operation.RequestBody?.Content?.TryGetValue("application/json", out var media) == true && media is OpenApiMediaType json)
            {
                json.Examples = ToOpenApi(examples);
            }

            return Task.CompletedTask;
        });

    /// <summary>Los ejemplos con la forma del documento OpenAPI; cada valor es una copia, para no compartir nodos.</summary>
    public static Dictionary<string, IOpenApiExample> ToOpenApi(IEnumerable<RequestExample> examples) =>
        examples.ToDictionary(
            example => example.Name,
            IOpenApiExample (example) => new OpenApiExample
            {
                Summary = example.Summary,
                Description = example.Description,
                Value = example.Value.DeepClone(),
            });

    /// <summary>
    /// Pone un valor de ejemplo en los parámetros conocidos de la ruta y de la consulta, para que Scalar los muestre
    /// llenos: el NIT y el dígito de verificación de la empresa de ejemplo, el set de pruebas, identificadores, etc.
    /// </summary>
    public static void DescribeParameters(OpenApiOperation operation, string? relativePath)
    {
        foreach (var parameter in operation.Parameters?.OfType<OpenApiParameter>() ?? [])
        {
            parameter.Example ??= ParameterExample(parameter, relativePath ?? "");
        }
    }

    private static JsonNode? ParameterExample(OpenApiParameter parameter, string relativePath) => parameter.Name switch
    {
        "nit" => Nit,
        "dv" => Dv,
        "testSetId" => TestSetId,
        "id" when parameter.Schema?.Format == "uuid" => Id,
        "id" => 1,
        "trackId" when relativePath.Contains("/zip/", StringComparison.Ordinal) => ZipKey,
        "trackId" or "cufe" => Cufe,
        "kind" => "pdf",
        "type" => 1,
        "prefix" => "SETP",
        "software_id" => ExampleBodies.SoftwareId,
        "desde" => "2026-11-01",
        "hasta" => "2026-11-30",
        "page" => 1,
        "X-Client" => "panel",
        _ => null,
    };

    /// <summary>Lee el <c>input.json</c> de un caso golden incrustado por el proyecto (ver el <c>.csproj</c>).</summary>
    private static RequestExample Golden(string goldenCase, string summary, string? description = null)
    {
        var resource = $"golden/{goldenCase}/input.json";
        using var stream = typeof(OpenApiExamples).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The embedded golden example {resource} is missing.");
        var value = JsonNode.Parse(stream) ?? throw new InvalidOperationException($"The golden example {resource} is empty.");
        return new RequestExample(goldenCase[(goldenCase.IndexOf('/', StringComparison.Ordinal) + 1)..], summary, value, description);
    }
}
