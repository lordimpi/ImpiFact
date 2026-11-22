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
        Variant("health-sector-one-user", "Factura del sector salud sin RIPS: SS-CUFE, un usuario y su copago (`health_fields`)", body =>
        {
            body["health_fields"] = JsonNode.Parse("""
                {
                  "invoice_period_start_date": "2026-11-01",
                  "invoice_period_end_date": "2026-11-14",
                  "health_type_operation_id": 1,
                  "users_info": [
                    {
                      "provider_code": "1100100001",
                      "health_contracting_payment_method_id": 4,
                      "health_coverage_id": 16,
                      "contract_number": "CUCON-EJEMPLO-0001",
                      "co_payment": "3300.00"
                    }
                  ]
                }
                """);
            body["prepaid_payment"] = JsonNode.Parse("""
                { "idpayment": "1", "prepaid_payment_type_id": 1, "paidamount": "3300.00", "receiveddate": "2026-11-14" }
                """);
            body["legal_monetary_totals"]!["pre_paid_amount"] = "3300.00";
        }) with
        {
            Description = "Parte de la factura simple (`single-line-iva19`) sin `number`. Reemplace `provider_code` por el código " +
                "REPS del prestador y `contract_number` por el CUCON del contrato; el copago va como anticipo con concepto 1.",
        },
    ];

    /// <summary>
    /// The invoice variants start from the simple invoice example with the fields of each variant added, and without
    /// <c>number</c>, so that they take the next number after the standard invoice examples.
    /// </summary>
    public static IReadOnlyList<RequestExample> AiuInvoices { get; } =
    [
        Variant("aiu-administration", "Factura AIU: la línea «Administración» con el objeto del contrato (`noteAIU`)", body =>
        {
            body["noteAIU"] = "OBRA CIVIL DE EJEMPLO";
            body["invoice_lines"]![0]!["description"] = "Administración";
        }),
    ];

    public static IReadOnlyList<RequestExample> MandateInvoices { get; } =
    [
        Variant("mandate-third-party-line", "Factura de mandato: una línea vendida por cuenta de un tercero (`agentparty`)", body =>
        {
            body["invoice_lines"]![0]!["agentparty"] = "800000005";
            body["invoice_lines"]![0]!["agentparty_dv"] = "9";
        }),
    ];

    public static IReadOnlyList<RequestExample> TransportInvoices { get; } =
    [
        Variant("transport-rndc-consignment", "Factura de transporte de carga: una remesa del RNDC (`is_RNDC`)", body =>
        {
            var line = body["invoice_lines"]![0]!;
            line["is_RNDC"] = true;
            line["RNDC_consignment_number"] = "95000912001";
            line["internal_consignment_number"] = "REM000001";
            line["value_consignment"] = "1500000";
            line["unit_measure_consignment_id"] = 767;
            line["quantity_consignment"] = "200.00";
        }),
    ];

    /// <summary>
    /// The export examples start from the simple invoice too (the project embeds only the standard goldens): type 2, a
    /// buyer in the United States, IVA 0 %, an Incoterm and the supplement K, in pesos and in dollars.
    /// </summary>
    public static IReadOnlyList<RequestExample> ExportInvoices { get; } =
    [
        Export("export-cop-cif", "Factura de exportación en pesos: CIF, comprador en Estados Unidos y suplemento K", "CIF",
            "COSTO SEGURO Y FLETE", currency: null),
        Export("export-usd-fob", "Factura de exportación en dólares (USD): FOB, con la tasa de cambio (`calculationrate`)", "FOB",
            "LIBRE A BORDO", currency: ("4100.50", "2026-11-14")),
    ];

    /// <summary>
    /// The contingency type 03 example transcribes a paper invoice of the type 3 resolution example, so it carries the number
    /// printed on that paper invoice and its time.
    /// </summary>
    public static IReadOnlyList<RequestExample> ContingencyInvoices { get; } =
    [
        Variant("contingency-paper-backup", "Factura de contingencia (tipo 03): transcripción de la factura de papel CTG201", body =>
        {
            body["type_document_id"] = 3;
            body["number"] = 990000201;
            body["AdditionalDocumentReferenceID"] = "CTG201";
            body["AdditionalDocumentReferenceDate"] = "2026-11-14";
            body["AdditionalDocumentReferenceTypeDocument"] = "01";
        }) with
        {
            Description = "Parte de la factura simple (`single-line-iva19`). `number` es el de la factura de papel (del rango de la " +
                "resolución de tipo 3) y `AdditionalDocumentReference*`, su número, su fecha y su tipo de documento (`01`).",
        },
    ];

    /// <summary>The invoice type 04 example is the simple invoice with type 12 and a note; it takes the next invoice number.</summary>
    public static IReadOnlyList<RequestExample> DianContingencyInvoices { get; } =
    [
        Variant("dian-contingency", "Factura tipo 04: emitida mientras la DIAN no responde", body =>
        {
            body["type_document_id"] = 12;
            body["notes"] = "FACTURA EXPEDIDA EN CONTINGENCIA";
        }),
    ];

    public static IReadOnlyList<RequestExample> CreditNotes { get; } =
    [
        Golden("credit-note/partial-return-with-reference", "Devolución parcial de la factura SETP990000001",
            "Referencia por su CUFE la factura del ejemplo «con descuentos» (SETP990000001), que debe estar aceptada por la DIAN."),
        Golden("credit-note/without-reference-invoice-period", "Rebaja sin factura de referencia, por periodo (`invoice_period`)"),
    ];

    /// <summary>
    /// Bodies for <c>addDocumentToBatch</c>: the simple invoice without <c>number</c> (type <c>invoice</c>) and the credit note
    /// without a reference invoice (type <c>credit-note</c>), so both go in the same batch.
    /// </summary>
    public static IReadOnlyList<RequestExample> BatchDocuments { get; } =
    [
        Variant("batch-invoice", "type invoice: la factura simple, sin `number`", _ => { }),
        Golden("credit-note/without-reference-invoice-period", "type credit-note: rebaja sin factura de referencia, por periodo") with
        {
            Name = "batch-credit-note",
        },
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
        "kind" when relativePath.Contains("/files/", StringComparison.Ordinal) => "pdf",
        "kind" => "invoice",
        "customer" => "Consumidor final",
        "number" when relativePath.Contains("customers/lookup", StringComparison.Ordinal) => Nit,
        "number" => "SETP990000001",
        "from" => "2026-11-01",
        "to" => "2026-11-30",
        "type" => 1,
        "prefix" => "SETP",
        "software_id" => ExampleBodies.SoftwareId,
        "desde" => "2026-11-01",
        "hasta" => "2026-11-30",
        "page" => 1,
        "unread" => true,
        "name" when relativePath.Contains("catalogs/", StringComparison.Ordinal) => "municipalities",
        "q" when relativePath.Contains("catalogs/", StringComparison.Ordinal) => "bogota",
        "X-Client" => "panel",
        _ => null,
    };

    /// <summary>The simple invoice golden example without <c>number</c> and with the changes of a variant.</summary>
    private static RequestExample Variant(string name, string summary, Action<JsonNode> change)
    {
        var value = Golden("invoice/single-line-iva19", summary).Value.DeepClone();
        value.AsObject().Remove("number");
        change(value);
        return new RequestExample(name, summary, value,
            "Parte de la factura simple (`single-line-iva19`) sin `number`, así toma el siguiente número de la resolución.");
    }

    /// <summary>
    /// An export invoice from the simple invoice: amounts in the document currency (pesos, or dollars with the exchange
    /// rate and its date) and the supplement K with the totals converted to pesos at that rate.
    /// </summary>
    private static RequestExample Export(string name, string summary, string incoterm, string terms, (string Rate, string Date)? currency)
    {
        const decimal amount = 1500000.00m;
        var rate = currency is { } foreign ? decimal.Parse(foreign.Rate, System.Globalization.CultureInfo.InvariantCulture) : 1m;
        string Pesos(decimal value) => (value * rate).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        var example = Variant(name, summary, body =>
        {
            body["type_document_id"] = 2;
            body["customer"] = new JsonObject
            {
                ["identification_number"] = "XE0000000001",
                ["type_document_identification_id"] = 8,
                ["type_organization_id"] = 2,
                ["type_regime_id"] = 2,
                ["type_liability_id"] = 117,
                ["name"] = "COMPRADOR EXTRANJERO DE EJEMPLO",
                ["phone"] = "3000000002",
                ["address"] = "100 MAIN STREET",
                ["email"] = "comprador@example.com",
                ["merchant_registration"] = "0000-00",
                ["country_id"] = 65,
                ["municipality_name"] = "MIAMI",
                ["state_name"] = "FLORIDA",
            };
            body["deliveryterms"] = new JsonObject
            {
                ["special_terms"] = terms, ["loss_risk_responsibility_code"] = incoterm, ["loss_risk"] = terms,
            };
            if (currency is { } foreign)
            {
                body["idcurrency"] = 149;
                body["calculationrate"] = foreign.Rate;
                body["calculationratedate"] = foreign.Date;
            }

            // Exports are exempt: IVA at 0 %.
            body["tax_totals"]![0]!["tax_amount"] = "0.00";
            body["tax_totals"]![0]!["percent"] = "0.00";
            var line = body["invoice_lines"]![0]!;
            line["tax_totals"]![0]!["tax_amount"] = "0.00";
            line["tax_totals"]![0]!["percent"] = "0.00";
            line["brandname"] = "MARCA DE EJEMPLO";
            line["modelname"] = "MODELO 1";
            body["legal_monetary_totals"]!["tax_inclusive_amount"] = "1500000.00";
            body["legal_monetary_totals"]!["payable_amount"] = "1500000.00";

            var supplement = new JsonObject
            {
                ["responsible_incharge"] = "RESPONSABLE DE EJEMPLO",
                ["departure_place"] = "BOGOTÁ",
                ["conveyance"] = "AVION",
                ["transport_document_type"] = "GUIA AEREA",
                ["transport_document_number"] = "AWB-000001",
                ["transporter_processor"] = "TRANSPORTADORA DE EJEMPLO",
                ["merchandise_origin_country"] = "COLOMBIA",
                ["destination"] = "MIAMI",
                ["payment_means"] = "CONTADO",
                ["insurance_carrier"] = "ASEGURADORA DE EJEMPLO",
                ["observations"] = "NINGUNA",
                ["FctConvCop"] = currency?.Rate ?? "1.00",
                ["MonedaCop"] = currency is null ? "COP" : "USD",
            };
            foreach (var field in new[] { "SubTotalCop", "TotalBrutoFacturaCop", "TotalNetoFacturaCop", "VlrPagarCop" })
            {
                supplement[field] = Pesos(amount);
            }

            foreach (var field in new[]
            {
                "DescuentoDetalleCop", "RecargoDetalleCop", "TotIvaCop", "TotIncCop", "TotBolCop", "ImpOtroCop", "MntImpCop", "MntDctoCop",
                "MntRcgoCop", "ReteFueCop", "ReteIvaCop", "ReteIcaCop", "TotAnticiposCop",
            })
            {
                supplement[field] = "0.00";
            }

            body["k_supplement"] = supplement;
        });
        return example with
        {
            Description = "Parte de la factura simple (`single-line-iva19`) sin `number`: toma el siguiente número de la resolución " +
                "de exportación (tipo 2), que debe estar configurada." + (currency is null ? "" : " Los valores van en dólares."),
        };
    }

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
