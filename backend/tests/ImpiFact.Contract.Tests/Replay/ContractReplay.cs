using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Contract.Tests.Postman;
using ImpiFact.Integration.Tests.Companies;
using ImpiFact.Integration.Tests.DianSend;
using ImpiFact.Integration.Tests.Protection;

namespace ImpiFact.Contract.Tests.Replay;

/// <summary>
/// Reproduce las solicitudes de la colección en orden contra la API, como una sola sesión de Postman: la primera crea la
/// empresa y su API key; las siguientes la usan. Lo que en la colección son datos de la instalación de quien la grabó
/// se cambia por los de esta sesión (<see cref="Prepare"/>): servidor, NIT, credenciales, certificado, números de
/// documento (el PHP aceptaba números repetidos; ImpiFact no, inventario de reglas 3.4) y referencias a documentos
/// emitidos (CUFE, ZipKey, prefijo y número). Todo lo demás del cuerpo va tal cual.
/// </summary>
/// <remarks>
/// La emisión responde de inmediato con el documento en cola (decisión C5 de la fase 4): después de cada emisión se
/// ejecuta el outbox del Worker en el mismo proceso y se consulta el estado con <c>status/document</c>, que debe
/// decir aceptado.
/// </remarks>
public sealed partial class ContractReplay(CompanyApiFactory api, DianSendHost worker)
{
    /// <summary>NIT de la empresa de la colección, que aparece en rutas y cuerpos.</summary>
    private const string CollectionNit = "89008003";

    /// <summary>Set de pruebas de habilitación ficticio para obtener un ZipKey (la colección trae uno ajeno).</summary>
    private const string TestSetId = "7c1d2e3f-4a5b-4c6d-8e7f-0a1b2c3d4e5f";

    private readonly (string Nit, string Dv) company = CompaniesHost.NewNit();
    private readonly Dictionary<int, long> nextNumber = new()
    {
        [1] = 990000001, [2] = 990000001, [3] = 990000001, [4] = 1, [5] = 1, [11] = 984000000, [13] = 1,
    };
    private HttpClient? platform;
    private HttpClient? companyClient;
    private string? zipKey;
    private int? planId;

    /// <summary>La última factura aceptada: sus datos reemplazan las referencias a facturas de la colección.</summary>
    public IssuedDocument? LastInvoice { get; private set; }

    /// <summary>
    /// CUFE of the last invoice type 04, stored pending until «Transmitir las facturas de contingencia pendientes» sends it.
    /// </summary>
    private string? pendingContingencyCufe;

    /// <summary>The last accepted support document: it replaces the references of the adjustment note of the collection.</summary>
    public IssuedDocument? LastSupportDocument { get; private set; }

    /// <summary>
    /// La última factura aceptada que pidió el correo (<c>sendmail</c>): la usa <c>send-email</c>, porque ImpiFact
    /// reenvía con los destinatarios de la solicitud original (inventario de reglas 5.4.7).
    /// </summary>
    public IssuedDocument? LastMailedInvoice { get; private set; }

    public async Task<ReplayOutcome> RunAsync(PostmanRequest request)
    {
        platform ??= await api.ClientWithNewKeyAsync(null, ApiScopes.Platform);
        if (NeedsZipKey(request) && zipKey is null)
        {
            zipKey = await SendTestSetAsync();
        }

        var prepared = Prepare(request);
        var client = UsesPlatformCredential(request) ? platform : companyClient ?? platform;
        using var message = new HttpRequestMessage(new HttpMethod(request.Method), prepared.Path);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (prepared.Body is not null)
        {
            message.Content = new StringContent(prepared.Body, Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(message);
        var text = await response.Content.ReadAsStringAsync();
        var outcome = new ReplayOutcome(request, prepared.Path, (int)response.StatusCode, ParseObject(text), text);
        if (outcome.IsSuccess)
        {
            await AfterSuccessAsync(request, prepared, outcome);
        }

        return outcome;
    }

    /// <summary>Servidor, NIT y referencias de la sesión en la ruta, y el cuerpo sin comentarios con los datos de la sesión.</summary>
    private PreparedRequest Prepare(PostmanRequest request)
    {
        var segments = request.ApiPath.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            if (segments[i] == CollectionNit)
            {
                segments[i] = company.Nit;
                if (i + 1 < segments.Length && segments[i + 1].Length == 1 && char.IsAsciiDigit(segments[i + 1][0]))
                {
                    segments[++i] = company.Dv;
                }
            }
            else if (LastInvoice is not null && Cufe().IsMatch(segments[i]))
            {
                segments[i] = LastInvoice.Cufe;
            }
            else if (zipKey is not null && i > 0 && segments[i - 1] == "zip")
            {
                segments[i] = zipKey;
            }
            else if (LastInvoice is not null && segments[i] == "regeneratepdf" && i + 2 < segments.Length)
            {
                segments[i + 1] = LastInvoice.Prefix;
                segments[i + 2] = LastInvoice.Number.ToString(CultureInfo.InvariantCulture);
                i += 2;
            }
            else if (planId is not null && i > 0 && segments[i - 1] is "query" or "queryusersbyplan")
            {
                segments[i] = planId.Value.ToString(CultureInfo.InvariantCulture);
            }
            else if (LastInvoice is not null)
            {
                segments[i] = InvoiceFullNumber().Replace(segments[i], LastInvoice.FullNumber);
            }
        }

        var path = string.Join('/', segments);
        var json = request.JsonBody is null ? null : JsonNode.Parse(request.JsonBody);
        var issuedType = IssuedType(request);
        if (json is JsonObject body)
        {
            if (issuedType is { } type)
            {
                PrepareIssue(body, type);
            }
            else
            {
                PrepareOther(request, body);
            }
        }

        return new PreparedRequest(path, json?.ToJsonString(), issuedType);
    }

    private void PrepareIssue(JsonObject body, int documentType)
    {
        // The invoice type 04 (type 12) uses the numbering of the sales invoice (annex 1.9, 12.2).
        var number = nextNumber[documentType == 12 ? 1 : documentType]++;
        body["number"] = number;
        if (documentType is 11 or 13)
        {
            PrepareSupportDocument(body, documentType);
        }

        var referenced = documentType == 13 ? LastSupportDocument : LastInvoice;
        if (referenced is not null && body["billing_reference"] is JsonObject reference)
        {
            reference["number"] = referenced.FullNumber;
            reference["uuid"] = referenced.Cufe;
            reference["issue_date"] = referenced.Date;
        }
    }

    /// <summary>
    /// The support-document requests of folder 02 were recorded against other installations (prefixes DSP and DS, their
    /// resolution numbers and dates): they get the SEDS and NDS numbering of folder 01D and, when their date falls outside
    /// the 2022 validity of that SEDS resolution, its last day, because the date must be within the resolution validity
    /// (the PHP checks it too).
    /// </summary>
    private static void PrepareSupportDocument(JsonObject body, int documentType)
    {
        body["prefix"] = documentType == 11 ? "SEDS" : "NDS";
        body.Remove("resolution_number");
        if (documentType == 11)
        {
            body["resolution_number"] = "18760000001";
        }

        if (body["date"]?.GetValue<string>() is not { } date || !date.StartsWith("2022-", StringComparison.Ordinal))
        {
            body["date"] = "2022-12-31";
        }
    }

    private void PrepareOther(PostmanRequest request, JsonObject body)
    {
        if (IsCompanyRoute(request))
        {
            body["email"] = $"{company.Nit}@example.com";
        }

        if (request.ApiPath.EndsWith("/config/certificate", StringComparison.Ordinal))
        {
            body["certificate"] = Convert.ToBase64String(TestPfx.Rsa());
            body["password"] = TestPfx.Password;
        }

        // La colección supone planes de su instalación (el 1 y el 2); aquí se usa el que creó «Crear un plan».
        if (planId is not null && (IsPlanRoute(request) || body.ContainsKey("type_plan_id")))
        {
            body[IsPlanRoute(request) ? "id" : "type_plan_id"] = planId.Value;
        }

        var invoice = request.ApiPath.EndsWith("/send-email", StringComparison.Ordinal) ? LastMailedInvoice : LastInvoice;
        if (invoice is null)
        {
            return;
        }

        if (body.ContainsKey("cufe"))
        {
            body["cufe"] = invoice.Cufe;
        }

        if (body.ContainsKey("prefix") && body.ContainsKey("number"))
        {
            body["prefix"] = invoice.Prefix;
            body["number"] = body["number"] is JsonValue value && value.TryGetValue<string>(out _)
                ? invoice.Number.ToString(CultureInfo.InvariantCulture)
                : invoice.Number;
        }
    }

    private async Task AfterSuccessAsync(PostmanRequest request, PreparedRequest prepared, ReplayOutcome outcome)
    {
        if (IsCompanyRoute(request) && outcome.Body?["token"]?.GetValue<string>() is { } token && companyClient is null)
        {
            companyClient = api.ClientWith(token);
        }

        if (IsPlanRoute(request) && planId is null)
        {
            planId = outcome.Body?["plan"]?["id"]?.GetValue<int>();
        }

        if (IsSendPending(request))
        {
            await AfterSendPendingAsync(outcome);
            return;
        }

        if (prepared.IssuedType is not { } type)
        {
            return;
        }

        var cufe = (outcome.Body?["cufe"] ?? outcome.Body?["cude"] ?? outcome.Body?["cuds"])?.GetValue<string>();
        if (cufe is null)
        {
            outcome.Problems.Add("the issue response has no cufe, cude or cuds");
            return;
        }

        if (type == 12)
        {
            // php-rules 12.2: the type 04 is stored pending, signed and without any DIAN call. The Worker is not run here:
            // its clock is later than the API one, so the automatic attempt scheduled 30 minutes after the issue is due.
            if (await StatusOfAsync(cufe) is var pending && pending != "ContingencyPending")
            {
                outcome.Problems.Add($"status/document says '{pending}' instead of ContingencyPending");
            }

            pendingContingencyCufe = cufe;
            return;
        }

        await RunWorkerAsync();
        var status = await StatusOfAsync(cufe);
        if (status is not ("Accepted" or "AcceptedWithWarnings"))
        {
            outcome.Problems.Add($"after the Worker ran, status/document says '{status}' instead of Accepted");
            return;
        }

        if (type == 11)
        {
            var issued = JsonNode.Parse(prepared.Body!)!.AsObject();
            LastSupportDocument = new IssuedDocument("SEDS", issued["number"]!.GetValue<long>(), cufe, issued["date"]!.GetValue<string>());
        }

        if (type == 1)
        {
            var body = JsonNode.Parse(prepared.Body!)!.AsObject();
            var prefix = body["prefix"]?.GetValue<string>() ?? "SETP";
            var number = body["number"]!.GetValue<long>();
            LastInvoice = new IssuedDocument(prefix, number, cufe, body["date"]?.GetValue<string>() ?? "");
            if (body["sendmail"]?.GetValue<bool>() == true)
            {
                LastMailedInvoice = LastInvoice;
            }
        }
    }

    /// <summary>send_pendings queues the pending type 04 for the Worker, which sends it and must leave it accepted.</summary>
    private async Task AfterSendPendingAsync(ReplayOutcome outcome)
    {
        if (pendingContingencyCufe is null)
        {
            outcome.Problems.Add("send_pendings ran before any invoice type 04 was issued");
            return;
        }

        await RunWorkerAsync();
        var status = await StatusOfAsync(pendingContingencyCufe);
        if (status is not ("Accepted" or "AcceptedWithWarnings"))
        {
            outcome.Problems.Add($"after send_pendings and the Worker, status/document says '{status}' instead of Accepted");
        }
    }

    private static bool IsSendPending(PostmanRequest request) =>
        request.Method == "POST" && request.ApiPath.StartsWith("/api/ubl2.1/invoice-contingency-4/send_pendings", StringComparison.Ordinal);

    /// <summary>Ejecuta el outbox del Worker hasta que no quede nada listo para tomar.</summary>
    private async Task RunWorkerAsync()
    {
        for (var round = 0; round < 10 && await worker.RunOutboxAsync() > 0; round++)
        {
        }
    }

    private async Task<string?> StatusOfAsync(string cufe)
    {
        using var response = await companyClient!.PostAsync($"/api/ubl2.1/status/document/{cufe}", JsonContent("{}"));
        return ParseObject(await response.Content.ReadAsStringAsync())?["document"]?["status"]?.GetValue<string>();
    }

    /// <summary>
    /// La colección consulta un ZipKey de su propia instalación. Para tener uno de esta sesión se emite una factura en
    /// el set de pruebas (<c>invoice/{testSetId}</c>), con el cuerpo de la factura más sencilla, y se ejecuta el Worker,
    /// que la envía con SendTestSetAsync y guarda el ZipKey.
    /// </summary>
    private async Task<string> SendTestSetAsync()
    {
        var invoice = JsonNode.Parse(SimpleInvoiceBody)!.AsObject();
        invoice["number"] = nextNumber[1]++;
        using var response = await companyClient!.PostAsync($"/api/ubl2.1/invoice/{TestSetId}", JsonContent(invoice.ToJsonString()));
        Assert.That(response.IsSuccessStatusCode, Is.True, await response.Content.ReadAsStringAsync());
        await RunWorkerAsync();
        // SendTestSetAsync no decide: el Worker programa GetStatusZip para un minuto después (SendDocumentToDianHandler).
        worker.Clock.Advance(TimeSpan.FromMinutes(1));
        await RunWorkerAsync();
        return ContractHost.ZipKey;
    }

    private static StringContent JsonContent(string json) => new(json, Encoding.UTF8, "application/json");

    private static bool NeedsZipKey(PostmanRequest request) => request.ApiPath.Contains("/status/zip/", StringComparison.Ordinal);

    /// <summary>
    /// El alta de empresa y las rutas de plataforma usan la API key de plataforma, que en ImpiFact reemplaza al
    /// <c>X-Admin-Token</c> del PHP (ADR-06 y ADR-12); el resto usa la API key de la empresa en <c>Authorization: Bearer</c>,
    /// como el <c>{{token}}</c> de la colección. Las rutas públicas del PHP también la llevan: en ImpiFact todas piden
    /// credencial.
    /// </summary>
    private static bool UsesPlatformCredential(PostmanRequest request) => IsCompanyRoute(request) || request.HasHeader("X-Admin-Token");

    private static bool IsPlanRoute(PostmanRequest request) => request.Method == "PUT" && request.ApiPath == "/api/ubl2.1/plan";

    private static bool IsCompanyRoute(PostmanRequest request) =>
        request.Method == "POST" && CompanyRoute().IsMatch(request.ApiPath);

    /// <summary>
    /// Tipo de documento que emite la solicitud (1 factura, 2 exportación, 3 contingencia tipo 03, 4 nota crédito, 5 nota
    /// débito, 11 documento soporte, 12 factura tipo 04, 13 nota de ajuste); nulo si no emite.
    /// </summary>
    private static int? IssuedType(PostmanRequest request) => request.Method != "POST" ? null : request.ApiPath switch
    {
        "/api/ubl2.1/support-document" => 11,
        "/api/ubl2.1/sd-credit-note" => 13,
        "/api/ubl2.1/invoice" or "/api/ubl2.1/invoice-aiu" or "/api/ubl2.1/invoice-mandate" or "/api/ubl2.1/invoice-transport" => 1,
        "/api/ubl2.1/invoice-export" => 2,
        "/api/ubl2.1/invoice-contingency" => 3,
        "/api/ubl2.1/invoice-contingency-4" => 12,
        "/api/ubl2.1/credit-note" => 4,
        "/api/ubl2.1/debit-note" => 5,
        _ => null,
    };

    private static JsonObject? ParseObject(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Factura mínima del anexo A (las 11 claves del PRD), para el set de pruebas.</summary>
    private const string SimpleInvoiceBody = """
        {"type_document_id":1,"date":"2025-07-22","time":"04:08:12","resolution_number":"18760000001","prefix":"SETP",
         "customer":{"identification_number":"222222222222","name":"Consumidor final"},
         "payment_form":{"payment_form_id":1,"payment_method_id":10},
         "legal_monetary_totals":{"line_extension_amount":"1000.00","tax_exclusive_amount":"1000.00","tax_inclusive_amount":"1190.00","payable_amount":"1190.00"},
         "tax_totals":[{"tax_id":1,"tax_amount":"190.00","percent":"19.00","taxable_amount":"1000.00"}],
         "invoice_lines":[{"unit_measure_id":70,"invoiced_quantity":"1","line_extension_amount":"1000.00","free_of_charge_indicator":false,
           "description":"Producto de prueba","code":"P1","type_item_identification_id":4,"price_amount":"1000.00","base_quantity":"1",
           "tax_totals":[{"tax_id":1,"tax_amount":"190.00","taxable_amount":"1000.00","percent":"19.00"}]}]}
        """;

    [GeneratedRegex("^/api/ubl2\\.1/config/[0-9]+(/[0-9])?$")]
    private static partial Regex CompanyRoute();

    [GeneratedRegex("^[0-9a-f]{96}$")]
    private static partial Regex Cufe();

    [GeneratedRegex("SETP[0-9]+")]
    private static partial Regex InvoiceFullNumber();

    private sealed record PreparedRequest(string Path, string? Body, int? IssuedType);
}

/// <summary>Un documento emitido y aceptado en la sesión.</summary>
public sealed record IssuedDocument(string Prefix, long Number, string Cufe, string Date)
{
    public string FullNumber => Prefix + Number.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Lo que respondió la API a una solicitud de la colección.</summary>
/// <param name="Path">Ruta enviada, ya con los datos de la sesión.</param>
/// <param name="Body">Cuerpo de la respuesta si es un objeto JSON.</param>
public sealed record ReplayOutcome(PostmanRequest Request, string Path, int Status, JsonObject? Body, string Text)
{
    public bool IsSuccess => Status is >= 200 and < 300;

    /// <summary>Problemas encontrados después de responder (por ejemplo, el estado tras ejecutar el Worker).</summary>
    public List<string> Problems { get; } = [];
}
