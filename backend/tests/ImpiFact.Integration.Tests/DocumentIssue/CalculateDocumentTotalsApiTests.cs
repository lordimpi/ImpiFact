using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Documents;
using ImpiFact.Integration.Tests.Companies;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Integration.Tests.DocumentIssue;

/// <summary>
/// <c>POST /api/v1/documents/calculate</c> por la API real y con PostgreSQL: el panel calcula los valores del borrador,
/// los envía tal cual a la emisión y la emisión los acepta; el cálculo no crea documentos ni toma consecutivo y pide una
/// credencial de empresa con <c>documents:write</c>.
/// </summary>
[Category("Integration")]
public sealed class CalculateDocumentTotalsApiTests
{
    private const string CalculateUrl = "/api/v1/documents/calculate";

    private static CompanyApiFactory Api => DocumentIssueHost.Api;

    /// <summary>
    /// Borrador con todo lo que cambia el cálculo: precio con 3 decimales por una cantidad fraccionaria, descuento de
    /// línea por porcentaje, IVA del 19 % y del 5 %, impuesto a las bolsas por unidad, una muestra gratuita y un flete
    /// global.
    /// </summary>
    private static readonly JsonObject Draft = JsonNode.Parse("""
        {
          "lines": [
            { "quantity": 3.333333, "unit_price": 1234.567, "taxes": [{ "tax_id": 1, "percent": 19 }] },
            {
              "quantity": 7,
              "unit_price": 12500.75,
              "allowance_charges": [{ "charge_indicator": false, "percent": 10 }],
              "taxes": [{ "tax_id": 1, "percent": 5 }]
            },
            { "quantity": 3, "unit_price": 0, "taxes": [{ "tax_id": 10, "per_unit_amount": 66, "unit_measure_id": 70 }] },
            { "quantity": 1, "unit_price": 65000, "free_of_charge": true }
          ],
          "allowance_charges": [{ "charge_indicator": true, "amount": 35000 }]
        }
        """)!.AsObject();

    [Test]
    public async Task The_calculated_values_sent_as_they_are_issue_an_invoice_and_calculating_creates_nothing()
    {
        using var issuer = await NewIssuerAsync();

        using var calculated = await issuer.Client.PostAsJsonAsync(CalculateUrl, Draft);
        var text = await calculated.Content.ReadAsStringAsync();
        Assert.That(calculated.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
        await using (var scope = Api.OpenScope(null, null, ActorType.System))
        {
            Assert.That(await CompanyApiTests.Rows<Document>(scope.Db, issuer.CompanyId).CountAsync(), Is.Zero, "Calculating creates no document.");
        }

        var calculation = JsonNode.Parse(text)!.AsObject();
        var totals = calculation["legal_monetary_totals"]!;
        Assert.Multiple(() =>
        {
            Assert.That(calculation["lines"]!.AsArray().Select(line => line!["line_extension_amount"]!.ToJsonString()),
                Is.EqualTo(new[] { "4115.22", "78754.72", "0.00", "0.00" }));
            Assert.That(totals["line_extension_amount"]!.ToJsonString(), Is.EqualTo("82869.94"));
            Assert.That(totals["tax_exclusive_amount"]!.ToJsonString(), Is.EqualTo("82869.94"));
            Assert.That(totals["tax_inclusive_amount"]!.ToJsonString(), Is.EqualTo("87787.57"));
            Assert.That(totals["charge_total_amount"]!.ToJsonString(), Is.EqualTo("35000.00"));
            Assert.That(totals["payable_amount"]!.ToJsonString(), Is.EqualTo("122787.57"));
            Assert.That(calculation["tax_totals"]!.AsArray().Select(group => $"{group!["tax_id"]}={group["tax_amount"]!.ToJsonString()}"),
                Is.EqualTo(new[] { "1=4719.63", "10=198.00" }));
        });

        using var issued = await PostIssueAsync(issuer.Client, IssueBody(calculation));

        Assert.That(issued.StatusCode, Is.EqualTo(HttpStatusCode.OK), await issued.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task Calculating_needs_a_company_credential_with_documents_write()
    {
        using var issuer = await NewIssuerAsync();
        using var readOnly = await Api.ClientWithNewKeyAsync(issuer.CompanyId, ApiScopes.DocumentsRead);
        using var withoutCompany = await Api.ClientWithNewKeyAsync(null, ApiScopes.DocumentsWrite);
        using var anonymous = Api.CreateClient();

        using var forbidden = await readOnly.PostAsJsonAsync(CalculateUrl, Draft);
        using var noCompany = await withoutCompany.PostAsJsonAsync(CalculateUrl, Draft);
        using var unauthenticated = await anonymous.PostAsJsonAsync(CalculateUrl, Draft);

        var noCompanyCode = await Code(noCompany);
        Assert.Multiple(() =>
        {
            Assert.That(forbidden.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(noCompany.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(noCompanyCode, Is.EqualTo("tenant.company_required"));
            Assert.That(unauthenticated.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        });
    }

    [Test]
    public async Task An_invalid_draft_answers_422_with_the_field_path()
    {
        using var issuer = await NewIssuerAsync();
        var draft = JsonNode.Parse("""{ "lines": [{ "quantity": 0, "unit_price": 10, "taxes": [{ "tax_id": 1 }] }] }""");

        using var response = await issuer.Client.PostAsJsonAsync(CalculateUrl, draft);

        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), text);
        using var json = JsonDocument.Parse(text);
        var errors = json.RootElement.GetProperty("errors");
        Assert.Multiple(() =>
        {
            Assert.That(json.RootElement.GetProperty("code").GetString(), Is.EqualTo("validation.failed"));
            Assert.That(errors.GetProperty("lines[0].quantity")[0].GetString(), Is.EqualTo("La cantidad debe ser mayor que cero."));
            Assert.That(errors.GetProperty("lines[0].taxes[0]")[0].GetString(), Is.EqualTo("El impuesto debe llevar percent o per_unit_amount."));
        });
    }

    [Test]
    public async Task Missing_or_null_lists_count_as_empty()
    {
        using var issuer = await NewIssuerAsync();
        var draft = JsonNode.Parse("""
            { "lines": [{ "quantity": 2, "unit_price": 10, "taxes": null }], "allowance_charges": null }
            """);

        using var response = await issuer.Client.PostAsJsonAsync(CalculateUrl, draft);

        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
        using var json = JsonDocument.Parse(text);
        Assert.That(json.RootElement.GetProperty("legal_monetary_totals").GetProperty("payable_amount").GetRawText(), Is.EqualTo("20.00"));
    }

    /// <summary>
    /// La factura del ejemplo golden <c>single-line-iva19</c> (comprador, pago, resolución) con las líneas del borrador y
    /// los valores calculados, sin número para que lo asigne el consecutivo. Es lo que hará el panel.
    /// </summary>
    private static byte[] IssueBody(JsonObject calculation)
    {
        var body = JsonNode.Parse(File.ReadAllText(Path.Combine(GoldenIssueTests.GoldenRoot(), "invoice", "single-line-iva19", "input.json")))!.AsObject();
        body.Remove("number");
        var lines = new JsonArray();
        var drafts = Draft["lines"]!.AsArray();
        var results = calculation["lines"]!.AsArray();
        for (var i = 0; i < drafts.Count; i++)
        {
            var draft = drafts[i]!.AsObject();
            var result = results[i]!.AsObject();
            var line = new JsonObject
            {
                ["unit_measure_id"] = 70,
                ["invoiced_quantity"] = draft["quantity"]!.ToJsonString(),
                ["line_extension_amount"] = result["line_extension_amount"]!.ToJsonString(),
                ["free_of_charge_indicator"] = draft["free_of_charge"]?.GetValue<bool>() ?? false,
                ["description"] = $"Producto {i + 1}",
                ["code"] = $"P{i + 1}",
                ["type_item_identification_id"] = 4,
                ["price_amount"] = draft["unit_price"]!.ToJsonString(),
                ["base_quantity"] = draft["quantity"]!.ToJsonString(),
                ["allowance_charges"] = Charges(result["allowance_charges"]!.AsArray()),
                ["tax_totals"] = Taxes(result["tax_subtotals"]!.AsArray()),
            };
            if (line["free_of_charge_indicator"]!.GetValue<bool>())
            {
                line["reference_price_id"] = 1;
            }

            lines.Add(line);
        }

        body["invoice_lines"] = lines;
        body["tax_totals"] = Taxes(new JsonArray(calculation["tax_totals"]!.AsArray()
            .SelectMany(group => group!["tax_subtotals"]!.AsArray()).Select(tax => tax!.DeepClone()).ToArray()));
        body["allowance_charges"] = Charges(calculation["allowance_charges"]!.AsArray());
        var totals = calculation["legal_monetary_totals"]!.AsObject();
        body["legal_monetary_totals"] = new JsonObject(totals
            .Where(pair => pair.Key != "pre_paid_amount")
            .Select(pair => KeyValuePair.Create(pair.Key, (JsonNode?)pair.Value!.ToJsonString())));
        return Encoding.UTF8.GetBytes(body.ToJsonString());
    }

    private static JsonArray Charges(JsonArray items) => new(items.Select(item => (JsonNode)new JsonObject
    {
        ["discount_id"] = item!["charge_indicator"]!.GetValue<bool>() ? 12 : 10,
        ["charge_indicator"] = item["charge_indicator"]!.GetValue<bool>(),
        ["allowance_charge_reason"] = item["charge_indicator"]!.GetValue<bool>() ? "Flete" : "Descuento",
        ["amount"] = item["amount"]!.ToJsonString(),
        ["base_amount"] = item["base_amount"]!.ToJsonString(),
    }).ToArray());

    /// <summary>Los subtotales con los nombres de <c>tax_totals</c> de la emisión, sin los campos nulos.</summary>
    private static JsonArray Taxes(JsonArray subtotals) => new(subtotals.Select(tax => (JsonNode)new JsonObject(tax!.AsObject()
        .Where(pair => pair.Value is not null)
        .Select(pair => KeyValuePair.Create(pair.Key, (JsonNode?)(pair.Key is "tax_id" or "unit_measure_id"
            ? pair.Value!.DeepClone()
            : pair.Value!.ToJsonString()))))).ToArray());

    private static async Task<HttpResponseMessage> PostIssueAsync(HttpClient client, byte[] body)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await client.PostAsync("/api/v1/documents/invoices", content);
    }

    private static async Task<string?> Code(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("code").GetString();
    }

    private static async Task<Issuer> NewIssuerAsync()
    {
        var (nit, dv) = CompaniesHost.NewNit();
        return await IssuerSetup.CreateAsync(nit, dv);
    }
}
