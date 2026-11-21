using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Identity;
using ImpiFact.Integration.Tests.Model;
using ImpiFact.Integration.Tests.Protection;
using Microsoft.EntityFrameworkCore;
using static ImpiFact.Integration.Tests.Companies.CompanyApiTests;

namespace ImpiFact.Integration.Tests.Companies;

/// <summary>
/// Lecturas de la configuración de la empresa para el panel (Arquitectura frontend, sección 16, punto 12): datos,
/// ambientes, software sin PIN, resoluciones sin clave técnica y el estado del paso a producción. Todas piden
/// <c>config:write</c> y solo leen la empresa de la credencial.
/// </summary>
[Category("Integration")]
public sealed class CompanyConfigurationReadTests
{
    private const string TechnicalKey = "fc8eac422eba16e22ffd8c6f94b3f40a6e38162c";

    private static CompanyApiFactory Api => CompaniesHost.Api;

    [Test]
    public async Task Company_data_comes_in_the_shape_the_update_accepts()
    {
        var (company, nit) = await CreateCompanyAsync();

        using var response = await company.GetAsync($"/api/v1/companies/{nit}");
        var body = await response.Content.ReadAsStringAsync();
        var data = JsonNode.Parse(body)!.AsObject();
        data["business_name"] = "NOMBRE DESDE EL PANEL";
        using var update = await company.PutAsJsonAsync($"/api/v1/companies/{nit}/{data["dv"]}", data);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
        Assert.Multiple(() =>
        {
            Assert.That(data["identification_number"]!.GetValue<string>(), Is.EqualTo(nit));
            Assert.That(data["email"]!.GetValue<string>(), Is.EqualTo($"{nit}@example.com"));
            Assert.That(data["phone"]!.GetValue<string>(), Is.EqualTo("3216547"));
            Assert.That(data["merchant_registration"]!.GetValue<string>(), Is.EqualTo("1234567-12"));
            Assert.That(data["municipality_id"]!.GetValue<int>(), Is.EqualTo(1006));
            Assert.That(data["type_regime_id"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(data["tax_id"]!.GetValue<int>(), Is.EqualTo(15));
            Assert.That(data["type_liability_ids"]!.AsArray().Select(id => id!.GetValue<int>()), Is.EquivalentTo(new[] { 7, 14 }));
            Assert.That(data["environments"]!.AsArray(), Has.Count.EqualTo(5));
            Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), "The read body is accepted by updateCompany as is.");
        });
        using var again = await Json(await company.GetAsync($"/api/v1/companies/{nit}"));
        Assert.That(again.RootElement.GetProperty("business_name").GetString(), Is.EqualTo("NOMBRE DESDE EL PANEL"));
    }

    [Test]
    public async Task Environments_list_every_family_and_follow_the_changes()
    {
        var (company, nit) = await CreateCompanyAsync();

        using var before = await Json(await company.GetAsync($"/api/v1/companies/{nit}/environments"));
        (await company.PutAsJsonAsync($"/api/v1/companies/{nit}/software", new { id = "soft-env", pin = "12345" })).Dispose();
        (await company.PutAsJsonAsync($"/api/v1/companies/{nit}/environment", new { type_environment_id = 1 })).Dispose();
        using var after = await Json(await company.GetAsync($"/api/v1/companies/{nit}/environments"));

        var initial = Environments(before.RootElement);
        var changed = Environments(after.RootElement);
        Assert.Multiple(() =>
        {
            Assert.That(initial.Select(environment => environment.Kind), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
            Assert.That(initial.Select(environment => environment.Type), Is.All.EqualTo(2));
            Assert.That(changed.Single(environment => environment.Kind == 1).Type, Is.EqualTo(1));
            Assert.That(changed.Where(environment => environment.Kind != 1).Select(environment => environment.Type), Is.All.EqualTo(2));
        });
    }

    [Test]
    public async Task Software_read_says_whether_there_is_a_pin_and_never_returns_it()
    {
        var (company, nit) = await CreateCompanyAsync();

        using var empty = await Json(await company.GetAsync($"/api/v1/companies/{nit}/software"));
        (await company.PutAsJsonAsync($"/api/v1/companies/{nit}/software", new { id = "soft-123", pin = "54321", test_set_id = "set-1" })).Dispose();
        using var response = await company.GetAsync($"/api/v1/companies/{nit}/software");
        var body = await response.Content.ReadAsStringAsync();
        string cipher;
        await using (var scope = Api.OpenScope(null, null, ActorType.System))
        {
            cipher = (await Rows<CompanySoftware>(scope.Db, await CompanyIdAsync(scope.Db, nit)).SingleAsync()).PinCipher;
        }

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
        var software = JsonDocument.Parse(body).RootElement.GetProperty("software");
        Assert.Multiple(() =>
        {
            Assert.That(empty.RootElement.GetProperty("software").GetArrayLength(), Is.Zero);
            Assert.That(software.GetArrayLength(), Is.EqualTo(1));
            Assert.That(software[0].GetProperty("document_kind").GetInt32(), Is.EqualTo(1));
            Assert.That(software[0].GetProperty("type_environment_id").GetInt32(), Is.EqualTo(2));
            Assert.That(software[0].GetProperty("identifier").GetString(), Is.EqualTo("soft-123"));
            Assert.That(software[0].GetProperty("url").GetString(), Is.EqualTo("https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc"));
            Assert.That(software[0].GetProperty("test_set_id").GetString(), Is.EqualTo("set-1"));
            Assert.That(software[0].GetProperty("has_pin").GetBoolean(), Is.True);
            Assert.That(software[0].TryGetProperty("pin", out _), Is.False);
            Assert.That(body, Does.Not.Contain("54321").And.Not.Contain(cipher));
        });
    }

    [Test]
    public async Task Resolutions_come_with_the_next_number_and_what_is_left_but_without_the_technical_key()
    {
        var (company, nit) = await CreateCompanyAsync();
        (await company.PutAsJsonAsync($"/api/v1/companies/{nit}/resolution", InvoiceResolution())).Dispose();
        (await company.PutAsJsonAsync($"/api/v1/companies/{nit}/resolution", new { type_document_id = 4, prefix = "NC", from = 1, to = 100 })).Dispose();
        await using (var scope = Api.OpenScope(null, null, ActorType.System))
        {
            var companyId = await CompanyIdAsync(scope.Db, nit);
            var invoice = await Rows<Resolution>(scope.Db, companyId).SingleAsync(row => row.DocumentTypeId == 1);
            // Guardar la resolución ya creó su consecutivo; aquí se adelanta como si se hubieran emitido 10 documentos.
            var scopeKey = NumberingScope.ForResolution(invoice.Id);
            (await Rows<NumberingSequence>(scope.Db, companyId).SingleAsync(row => row.ScopeKey == scopeKey)).RaiseTo(990000010);
            await scope.Db.SaveChangesAsync();
        }

        using var response = await company.GetAsync($"/api/v1/companies/{nit}/resolutions");
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
        var items = JsonDocument.Parse(body).RootElement.GetProperty("resolutions").EnumerateArray().ToList();
        Assert.That(items, Has.Count.EqualTo(2));
        var (invoiceItem, note) = (items[0], items[1]);
        Assert.Multiple(() =>
        {
            Assert.That(invoiceItem.GetProperty("kind").GetString(), Is.EqualTo("invoice"));
            Assert.That(invoiceItem.GetProperty("type_document_id").GetInt32(), Is.EqualTo(1));
            Assert.That(invoiceItem.GetProperty("type_environment_id").GetInt32(), Is.EqualTo(2));
            Assert.That(invoiceItem.GetProperty("prefix").GetString(), Is.EqualTo("SETP"));
            Assert.That(invoiceItem.GetProperty("resolution").GetString(), Is.EqualTo("18760000001"));
            Assert.That(invoiceItem.GetProperty("resolution_date").GetString(), Is.EqualTo("2019-01-19"));
            Assert.That(invoiceItem.GetProperty("from").GetInt64(), Is.EqualTo(990000000));
            Assert.That(invoiceItem.GetProperty("to").GetInt64(), Is.EqualTo(995000000));
            Assert.That(invoiceItem.GetProperty("date_from").GetString(), Is.EqualTo("2019-01-19"));
            Assert.That(invoiceItem.GetProperty("date_to").GetString(), Is.EqualTo("2030-01-19"));
            Assert.That(invoiceItem.GetProperty("is_active").GetBoolean(), Is.True);
            Assert.That(invoiceItem.GetProperty("has_technical_key").GetBoolean(), Is.True);
            Assert.That(invoiceItem.GetProperty("next_number").GetInt64(), Is.EqualTo(990000010));
            Assert.That(invoiceItem.GetProperty("remaining").GetInt64(), Is.EqualTo(995000000 - 990000010 + 1));
            Assert.That(note.GetProperty("kind").GetString(), Is.EqualTo("credit_note"));
            Assert.That(note.GetProperty("has_technical_key").GetBoolean(), Is.False);
            Assert.That(note.GetProperty("next_number").GetInt64(), Is.EqualTo(1), "Without documents the next number is the start of the range.");
            Assert.That(note.GetProperty("remaining").GetInt64(), Is.EqualTo(100));
            Assert.That(note.GetProperty("date_to").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(body, Does.Not.Contain(TechnicalKey).And.Not.Contain("\"technical_key\""));
        });
    }

    [Test]
    public async Task Setup_status_of_a_new_company_shows_what_is_missing()
    {
        var (company, nit) = await CreateCompanyAsync();

        using var response = await company.GetAsync($"/api/v1/companies/{nit}/setup-status");
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
        var status = JsonDocument.Parse(body).RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(status.GetProperty("company_complete").GetBoolean(), Is.True);
            Assert.That(status.GetProperty("certificate_valid").GetBoolean(), Is.False);
            Assert.That(status.GetProperty("software_configured").GetBoolean(), Is.False);
            Assert.That(status.GetProperty("test_set_id_configured").GetBoolean(), Is.False);
            Assert.That(status.GetProperty("test_set_accepted").ValueKind, Is.EqualTo(JsonValueKind.Null), "No DIAN answer is stored yet.");
            Assert.That(Kinds(status), Is.EqualTo(new[] { ("invoice", 1, false), ("credit_note", 4, false), ("debit_note", 5, false) }));
            Assert.That(Environments(status).Select(environment => environment.Type), Is.All.EqualTo(2));
        });
    }

    [Test]
    public async Task Setup_status_turns_green_with_certificate_software_resolution_and_an_accepted_test_set()
    {
        var (company, nit) = await CreateCompanyAsync();
        (await company.PutAsJsonAsync($"/api/v1/companies/{nit}/certificate",
            new { certificate = Convert.ToBase64String(TestPfx.Rsa()), password = TestPfx.Password })).Dispose();
        (await company.PutAsJsonAsync($"/api/v1/companies/{nit}/software", new { id = "soft-ok", pin = "12345", test_set_id = "set-ok" })).Dispose();
        (await company.PutAsJsonAsync($"/api/v1/companies/{nit}/resolution", InvoiceResolution())).Dispose();
        (await company.PutAsJsonAsync($"/api/v1/companies/{nit}/resolution", new { type_document_id = 4, prefix = "NC", from = 1, to = 100 })).Dispose();
        await AddTestSetAnswersAsync(nit, false, true);

        using var status = await Json(await company.GetAsync($"/api/v1/companies/{nit}/setup-status"));

        var root = status.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("certificate_valid").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("software_configured").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("test_set_id_configured").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("test_set_accepted").GetBoolean(), Is.True, "One accepted answer is enough.");
            Assert.That(Kinds(root), Is.EqualTo(new[] { ("invoice", 1, true), ("credit_note", 4, true), ("debit_note", 5, false) }));
        });
    }

    [Test]
    public async Task Setup_status_reports_a_rejected_test_set_and_an_invoice_resolution_without_technical_key_or_out_of_validity()
    {
        var (company, nit) = await CreateCompanyAsync();
        var expired = InvoiceResolution();
        expired["date_to"] = "2025-12-31";
        (await company.PutAsJsonAsync($"/api/v1/companies/{nit}/resolution", expired)).Dispose();
        await AddTestSetAnswersAsync(nit, false);

        using var status = await Json(await company.GetAsync($"/api/v1/companies/{nit}/setup-status"));

        Assert.Multiple(() =>
        {
            Assert.That(status.RootElement.GetProperty("test_set_accepted").GetBoolean(), Is.False);
            Assert.That(Kinds(status.RootElement)[0], Is.EqualTo(("invoice", 1, false)), "The validity ended on 2025-12-31; today is 2026-01-15.");
        });
    }

    [TestCase("Set de prueba con identificador 00000000-0000-0000-0000-000000000000 se encuentra Aceptado", true)]
    [TestCase("Set de prueba con identificador 00000000-0000-0000-0000-000000000000 se encuentra en proceso", null)]
    [TestCase("Batch en proceso de validación.", null)]
    public async Task Setup_status_reads_the_test_set_answer_from_the_description(string description, bool? expected)
    {
        // Con el set aprobado, la DIAN contesta GetStatusZip con IsValid falso y «se encuentra Aceptado» (código 2).
        // Mientras procesa, también con IsValid falso: «Batch en proceso de validación.» o «se encuentra en proceso»;
        // eso no es un rechazo, el set todavía no tiene resultado (null).
        var (company, nit) = await CreateCompanyAsync();
        await AddTestSetAnswersAsync(nit, (false, "Batch en proceso de validación."), (false, description));

        using var status = await Json(await company.GetAsync($"/api/v1/companies/{nit}/setup-status"));

        var accepted = status.RootElement.GetProperty("test_set_accepted");
        Assert.That(accepted.ValueKind == JsonValueKind.Null ? (bool?)null : accepted.GetBoolean(), Is.EqualTo(expected));
    }

    [Test]
    public async Task Reads_of_another_nit_answer_the_same_403_whether_it_exists_or_not()
    {
        var (company, _) = await CreateCompanyAsync();
        var (_, otherNit) = await CreateCompanyAsync();
        var (missingNit, _) = CompaniesHost.NewNit();

        Assert.Multiple(async () =>
        {
            foreach (var path in ReadPaths)
            {
                using var existing = await company.GetAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, path, otherNit));
                using var missing = await company.GetAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, path, missingNit));
                Assert.That(existing.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), path);
                Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), path);
                Assert.That(await CodeAndDetail(existing), Is.EqualTo(await CodeAndDetail(missing)).And.StartWith("company.forbidden|"), path);
            }
        });
    }

    [Test]
    public async Task Reads_need_config_write()
    {
        var (_, nit) = await CreateCompanyAsync();
        Guid companyId;
        await using (var scope = Api.OpenScope(null, null, ActorType.System))
        {
            companyId = await CompanyIdAsync(scope.Db, nit);
        }

        using var reader = await Api.ClientWithNewKeyAsync(companyId, ApiScopes.DocumentsRead, ApiScopes.DocumentsWrite);

        Assert.Multiple(async () =>
        {
            foreach (var path in ReadPaths)
            {
                using var response = await reader.GetAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, path, nit));
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), path);
            }
        });
    }

    private static readonly string[] ReadPaths =
    [
        "/api/v1/companies/{0}",
        "/api/v1/companies/{0}/environments",
        "/api/v1/companies/{0}/software",
        "/api/v1/companies/{0}/resolutions",
        "/api/v1/companies/{0}/setup-status",
    ];

    private static Dictionary<string, object> InvoiceResolution() => new()
    {
        ["type_document_id"] = 1, ["prefix"] = "SETP", ["resolution"] = "18760000001", ["resolution_date"] = "2019-01-19",
        ["technical_key"] = TechnicalKey, ["from"] = 990000000, ["to"] = 995000000,
        ["date_from"] = "2019-01-19", ["date_to"] = "2030-01-19",
    };

    /// <summary>Guarda respuestas de la DIAN al set de pruebas (GetStatusZip), una por documento, como las deja el envío.</summary>
    private static Task AddTestSetAnswersAsync(string nit, params bool[] isValid) =>
        AddTestSetAnswersAsync(nit, [.. isValid.Select(valid => (valid, (string?)null))]);

    private static async Task AddTestSetAnswersAsync(string nit, params (bool Valid, string? Description)[] answers)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var companyId = await CompanyIdAsync(scope.Db, nit);
        var number = 990000100L;
        foreach (var (valid, description) in answers)
        {
            var document = ModelGraph.NewDocument(companyId, number++, cufe: null);
            scope.Db.Add(document);
            scope.Db.Add(new DianSubmission
            {
                CompanyId = companyId,
                DocumentId = document.Id,
                Attempt = 1,
                Operation = DianOperation.GetStatusZip,
                TypeEnvironmentId = 2,
                IsValid = valid,
                DianStatusDescription = description,
                StartedAt = CompaniesHost.Now,
                FinishedAt = CompaniesHost.Now,
            });
        }

        await scope.Db.SaveChangesAsync();
    }

    /// <summary>Código y detalle del error, sin el identificador de correlación, que cambia en cada solicitud.</summary>
    private static async Task<string> CodeAndDetail(HttpResponseMessage response)
    {
        using var json = await Json(response);
        return json.RootElement.GetProperty("code").GetString() + "|" + json.RootElement.GetProperty("detail").GetString();
    }

    private static List<(int Kind, int Type)> Environments(JsonElement root) =>
        root.GetProperty("environments").EnumerateArray()
            .Select(environment => (environment.GetProperty("document_kind").GetInt32(), environment.GetProperty("type_environment_id").GetInt32()))
            .ToList();

    private static List<(string Kind, int Type, bool Valid)> Kinds(JsonElement root) =>
        root.GetProperty("resolutions").EnumerateArray()
            .Select(item => (item.GetProperty("kind").GetString()!, item.GetProperty("type_document_id").GetInt32(), item.GetProperty("valid").GetBoolean()))
            .ToList();
}
