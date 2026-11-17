using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Integration.Tests.Companies;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Integration.Tests.Plans;

/// <summary>
/// Rutas de la carpeta 09 «Planes» de la colección Postman por la API real y con PostgreSQL: <c>PUT plan</c>,
/// <c>plan/query</c>, <c>plan/queryusersbyplan</c> y <c>plan/infoplanuser</c> con el JSON del PHP, sus alias canónicos
/// bajo <c>/api/v1/plans</c>, la asignación de un plan a una empresa y quién puede llamar cada una.
/// </summary>
[Category("Integration")]
public sealed class PlanApiTests
{
    // Hora fija de la API de pruebas: 15-ene-2026 10:00 UTC, 05:00 en Colombia.
    private const string NowLaravel = "2026-01-15T10:00:00.000000Z";
    private const string NowColombia = "2026-01-15 05:00:00";

    private static int planSequence = 10;

    private static CompanyApiFactory Api => PlansHost.Api;

    [Test]
    public async Task Put_plan_creates_the_plan_with_its_four_quantities_and_answers_like_the_php()
    {
        using var platform = await PlansHost.PlatformAsync();
        var id = NextId();

        using var response = await platform.PutAsJsonAsync("/api/ubl2.1/plan", new Dictionary<string, object?>
        {
            ["id"] = id, ["name"] = $"Mensual {id}", ["qty_docs_invoice"] = 100, ["qty_docs_payroll"] = "5",
            ["qty_docs_radian"] = 7, ["qty_docs_ds"] = 0, ["period"] = 1, ["observations"] = "Plan de prueba",
        });

        using var json = await JsonAsync(response, HttpStatusCode.OK);
        var root = json.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("message").GetString(), Is.EqualTo("Plan creado/actualizado con éxito"));
            Assert.That(PropertyNames(root.GetProperty("plan")), Is.EqualTo(new[]
            {
                "id", "name", "qty_docs_invoice", "qty_docs_payroll", "qty_docs_radian", "qty_docs_ds", "period", "state", "observations",
                "created_at", "updated_at",
            }));
            Assert.That(root.GetProperty("plan").ToString(), Is.EqualTo(PlanJson(id, $"Mensual {id}", 100, 5, 7, 0, 1, true, "Plan de prueba")));
        });
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var plan = await scope.Db.Set<Plan>().AsNoTracking().SingleAsync(row => row.Id == id);
        var quotas = await scope.Db.Set<PlanQuota>().AsNoTracking().Where(row => row.PlanId == id).OrderBy(row => row.DocumentKind)
            .Select(row => new { row.DocumentKind, row.Quantity }).ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That((plan.Name, plan.Period, plan.IsActive, plan.Observations), Is.EqualTo(($"Mensual {id}", PlanPeriod.Monthly, true, "Plan de prueba")));
            Assert.That(quotas.Select(row => (row.DocumentKind, row.Quantity)), Is.EqualTo(new[]
            {
                (DocumentKind.Invoice, 100L), (DocumentKind.Payroll, 5L), (DocumentKind.SupportDocument, 0L), (DocumentKind.Events, 7L),
            }));
        });
    }

    [Test]
    public async Task Put_plan_again_updates_it_and_keeps_the_name_when_none_is_sent()
    {
        using var platform = await PlansHost.PlatformAsync();
        var id = await CreatePlanAsync(platform, period: 1, invoices: 100);

        using var response = await platform.PutAsJsonAsync("/api/ubl2.1/plan", new
        {
            id, qty_docs_invoice = 250, qty_docs_payroll = 1, qty_docs_radian = 2, qty_docs_ds = 3, period = 3, state = false,
        });

        using var json = await JsonAsync(response, HttpStatusCode.OK);
        Assert.That(json.RootElement.GetProperty("plan").ToString(), Is.EqualTo(PlanJson(id, $"Plan {id}", 250, 1, 2, 3, 3, false, null)));
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var plan = await scope.Db.Set<Plan>().AsNoTracking().SingleAsync(row => row.Id == id);
        Assert.Multiple(async () =>
        {
            Assert.That((plan.Name, plan.Period, plan.IsActive), Is.EqualTo(($"Plan {id}", PlanPeriod.Package, false)));
            Assert.That(plan.RowVersion, Is.EqualTo(2));
            Assert.That(await scope.Db.Set<PlanQuota>().CountAsync(row => row.PlanId == id), Is.EqualTo(4));
            Assert.That(await scope.Db.Set<PlanQuota>().Where(row => row.PlanId == id && row.DocumentKind == DocumentKind.Invoice)
                .Select(row => row.Quantity).SingleAsync(), Is.EqualTo(250));
        });
    }

    [Test]
    public async Task Put_plan_validates_the_php_request_rules()
    {
        using var platform = await PlansHost.PlatformAsync();

        using var response = await platform.PutAsJsonAsync("/api/ubl2.1/plan", new { id = 1000, qty_docs_invoice = -1, period = 4 });

        using var json = await JsonAsync(response, HttpStatusCode.UnprocessableEntity);
        var errors = json.RootElement.GetProperty("errors");
        Assert.Multiple(() =>
        {
            Assert.That(errors.GetProperty("id")[0].GetString(), Is.EqualTo("id debe tener entre 1 y 3 dígitos."));
            Assert.That(errors.GetProperty("qty_docs_invoice")[0].GetString(), Is.EqualTo("qty docs invoice debe tener entre 1 y 10 dígitos."));
            Assert.That(errors.GetProperty("qty_docs_payroll")[0].GetString(), Is.EqualTo("El campo qty docs payroll es obligatorio."));
            Assert.That(errors.GetProperty("qty_docs_radian")[0].GetString(), Is.EqualTo("El campo qty docs radian es obligatorio."));
            Assert.That(errors.GetProperty("qty_docs_ds")[0].GetString(), Is.EqualTo("El campo qty docs ds es obligatorio."));
            Assert.That(errors.GetProperty("period")[0].GetString(), Is.EqualTo("period es inválido."));
            Assert.That(json.RootElement.GetProperty("success").GetBoolean(), Is.False);
        });

        using var empty = await platform.PutAsJsonAsync("/api/ubl2.1/plan", new { });
        using var emptyJson = await JsonAsync(empty, HttpStatusCode.UnprocessableEntity);
        var missing = emptyJson.RootElement.GetProperty("errors");
        Assert.Multiple(() =>
        {
            Assert.That(missing.GetProperty("id")[0].GetString(), Is.EqualTo("El campo id es obligatorio."));
            Assert.That(missing.GetProperty("qty_docs_invoice")[0].GetString(), Is.EqualTo("El campo qty docs invoice es obligatorio."));
            Assert.That(missing.GetProperty("period")[0].GetString(), Is.EqualTo("El campo period es obligatorio."));
        });
    }

    // Correcciones al PHP: un plan nuevo sin nombre respondía un 500, y la regla unique chocaba con el mismo plan.
    [Test]
    public async Task A_new_plan_needs_a_name_and_another_plan_cannot_take_it()
    {
        using var platform = await PlansHost.PlatformAsync();
        var existing = await CreatePlanAsync(platform);
        var id = NextId();

        using var unnamed = await platform.PutAsJsonAsync("/api/ubl2.1/plan", Body(id, name: null));
        using var taken = await platform.PutAsJsonAsync("/api/ubl2.1/plan", Body(id, name: $"Plan {existing}"));
        using var same = await platform.PutAsJsonAsync("/api/ubl2.1/plan", Body(existing, name: $"Plan {existing}"));

        using var unnamedJson = await JsonAsync(unnamed, HttpStatusCode.UnprocessableEntity);
        using var takenJson = await JsonAsync(taken, HttpStatusCode.UnprocessableEntity);
        Assert.Multiple(async () =>
        {
            Assert.That(unnamedJson.RootElement.GetProperty("errors").GetProperty("name")[0].GetString(), Is.EqualTo("El campo name es obligatorio."));
            Assert.That(takenJson.RootElement.GetProperty("errors").GetProperty("name")[0].GetString(), Is.EqualTo("name ya ha sido registrado."));
            Assert.That(same.StatusCode, Is.EqualTo(HttpStatusCode.OK), await same.Content.ReadAsStringAsync());
            await using var scope = Api.OpenScope(null, null, ActorType.System);
            Assert.That(await scope.Db.Set<Plan>().AnyAsync(row => row.Id == id), Is.False);
        });
    }

    [Test]
    public async Task Plan_query_lists_the_plans_or_returns_one_by_id_on_both_routes()
    {
        using var platform = await PlansHost.PlatformAsync();
        var id = await CreatePlanAsync(platform, period: 2, invoices: 40);

        using var all = await platform.GetAsync("/api/ubl2.1/plan/query");
        using var canonicalAll = await platform.GetAsync("/api/v1/plans");
        using var one = await platform.GetAsync($"/api/ubl2.1/plan/query/{id}");
        using var canonicalOne = await platform.GetAsync($"/api/v1/plans/{id}");
        using var missing = await platform.GetAsync("/api/ubl2.1/plan/query/999");

        using var allJson = await JsonAsync(all, HttpStatusCode.OK);
        using var oneJson = await JsonAsync(one, HttpStatusCode.OK);
        using var missingJson = await JsonAsync(missing, HttpStatusCode.NotFound);
        var expected = PlanJson(id, $"Plan {id}", 40, 0, 0, 0, 2, true, null);
        Assert.Multiple(async () =>
        {
            Assert.That(allJson.RootElement.GetProperty("message").GetString(), Is.EqualTo("Plan/es existe/n, informacion del/los plan/es:"));
            Assert.That(allJson.RootElement.GetProperty("data").EnumerateArray().Select(plan => plan.ToString()), Does.Contain(expected));
            Assert.That(oneJson.RootElement.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(oneJson.RootElement.GetProperty("data").ToString(), Is.EqualTo(expected));
            Assert.That(await canonicalAll.Content.ReadAsStringAsync(), Is.EqualTo(allJson.RootElement.ToString()));
            Assert.That(await canonicalOne.Content.ReadAsStringAsync(), Is.EqualTo(oneJson.RootElement.ToString()));
            Assert.That(missingJson.RootElement.GetProperty("code").GetString(), Is.EqualTo("plan.not_found"));
            Assert.That(missingJson.RootElement.GetProperty("message").GetString(), Is.EqualTo("No existe el plan con ID 999."));
        });
    }

    [Test]
    public async Task Assigning_a_plan_replaces_the_active_plan_of_the_company_and_queryusersbyplan_lists_it()
    {
        using var platform = await PlansHost.PlatformAsync();
        using var company = await PlansHost.CreateCompanyAsync(platform);
        var first = await CreatePlanAsync(platform);
        var second = await CreatePlanAsync(platform);
        using var firstAssigned = await platform.PutAsJsonAsync($"/api/v1/companies/{company.Nit}/plan", new { plan_id = first });

        using var assigned = await platform.PutAsJsonAsync($"/api/v1/companies/{company.Nit}/plan", new { plan_id = second });
        using var byPlan = await platform.GetAsync($"/api/ubl2.1/plan/queryusersbyplan/{second}");
        using var canonical = await platform.GetAsync($"/api/v1/plans/{second}/companies");
        using var previous = await platform.GetAsync($"/api/ubl2.1/plan/queryusersbyplan/{first}");

        using var assignedJson = await JsonAsync(assigned, HttpStatusCode.OK);
        using var byPlanJson = await JsonAsync(byPlan, HttpStatusCode.OK);
        using var previousJson = await JsonAsync(previous, HttpStatusCode.OK);
        var user = byPlanJson.RootElement.GetProperty("user");
        Assert.Multiple(async () =>
        {
            Assert.That(firstAssigned.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(assignedJson.RootElement.GetProperty("message").GetString(), Is.EqualTo("Plan asignado con éxito"));
            Assert.That(assignedJson.RootElement.GetProperty("plan").GetProperty("id").GetInt32(), Is.EqualTo(second));
            Assert.That(assignedJson.RootElement.GetProperty("start_plan_date").GetString(), Is.EqualTo(NowColombia));
            Assert.That(byPlanJson.RootElement.GetProperty("message").GetString(), Is.EqualTo($"Usuarios registrados al plan ID: {second}"));
            Assert.That(user.GetArrayLength(), Is.EqualTo(1));
            Assert.That(user[0].GetProperty("identification_number").GetString(), Is.EqualTo(company.Nit));
            Assert.That(user[0].GetProperty("name").GetString(), Is.EqualTo("EMPRESA DE PRUEBA GOLDEN SAS"));
            Assert.That(user[0].GetProperty("email").GetString(), Is.EqualTo($"{company.Nit}@example.com"));
            Assert.That(user[0].GetProperty("state").GetBoolean(), Is.True);
            Assert.That(user[0].GetProperty("type_plan_id").GetInt32(), Is.EqualTo(second));
            Assert.That(user[0].GetProperty("start_plan_date").GetString(), Is.EqualTo(NowColombia));
            Assert.That(previousJson.RootElement.GetProperty("user").GetArrayLength(), Is.Zero);
            Assert.That(await canonical.Content.ReadAsStringAsync(), Is.EqualTo(byPlanJson.RootElement.ToString()));
        });
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var assignments = await scope.Db.Set<CompanyPlan>().IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .Where(row => row.CompanyId == company.CompanyId).OrderBy(row => row.PlanId).Select(row => new { row.PlanId, row.IsActive }).ToListAsync();
        Assert.That(assignments.Select(row => (row.PlanId, row.IsActive)), Is.EqualTo(new[] { (first, false), (second, true) }));
    }

    [Test]
    public async Task Assigning_an_unknown_plan_or_to_an_unknown_company_answers_404()
    {
        using var platform = await PlansHost.PlatformAsync();
        using var company = await PlansHost.CreateCompanyAsync(platform);
        var plan = await CreatePlanAsync(platform);

        using var unknownPlan = await platform.PutAsJsonAsync($"/api/v1/companies/{company.Nit}/plan", new { plan_id = 998 });
        using var unknownCompany = await platform.PutAsJsonAsync("/api/v1/companies/100000001/plan", new { plan_id = plan });
        using var queryUnknown = await platform.GetAsync("/api/ubl2.1/plan/queryusersbyplan/998");

        Assert.Multiple(() =>
        {
            Assert.That(unknownPlan.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(unknownCompany.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(queryUnknown.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public async Task Infoplanuser_reports_the_current_plan_its_renovation_and_the_documents_left()
    {
        using var platform = await PlansHost.PlatformAsync();
        using var company = await PlansHost.CreateCompanyAsync(platform);
        var id = await CreatePlanAsync(platform, period: 1, invoices: 100, payroll: 10);
        using var assigned = await platform.PutAsJsonAsync($"/api/v1/companies/{company.Nit}/plan", new { plan_id = id });
        await AddUsageAsync(company.CompanyId, DocumentKind.Invoice, 3);

        using var response = await company.Client.GetAsync("/api/ubl2.1/plan/infoplanuser");
        using var canonical = await company.Client.GetAsync("/api/v1/plans/current");

        using var json = await JsonAsync(response, HttpStatusCode.OK);
        var root = json.RootElement;
        var plan = PlanJson(id, $"Plan {id}", 100, 10, 0, 0, 1, true, null);
        Assert.Multiple(async () =>
        {
            Assert.That(PropertyNames(root), Is.EqualTo(new[]
            {
                "success", "message", "plan", "period", "start_plan_date", "renovation_date", "plan2", "period2", "start_plan_date2",
                "renovation_date2", "plan3", "period3", "start_plan_date3", "renovation_date3", "plan4", "period4", "start_plan_date4",
                "renovation_date4", "docs_left_invoice", "docs_left_payroll", "docs_left_radian", "docs_left_ds",
            }));
            Assert.That(root.GetProperty("message").GetString(), Is.EqualTo($"Informacion del plan vigente para el usuario: {company.Nit}"));
            Assert.That(root.GetProperty("plan").ToString(), Is.EqualTo(plan));
            Assert.That(root.GetProperty("plan4").ToString(), Is.EqualTo(plan));
            Assert.That(root.GetProperty("period").GetString(), Is.EqualTo("MENSUAL"));
            Assert.That(root.GetProperty("start_plan_date").GetString(), Is.EqualTo(NowColombia));
            Assert.That(root.GetProperty("renovation_date").GetString(), Is.EqualTo("2026-02-15 05:00:00"));
            Assert.That(root.GetProperty("docs_left_invoice").GetInt64(), Is.EqualTo(97));
            Assert.That(root.GetProperty("docs_left_payroll").GetInt64(), Is.EqualTo(10));
            Assert.That(root.GetProperty("docs_left_radian").GetInt64(), Is.Zero);
            Assert.That(await canonical.Content.ReadAsStringAsync(), Is.EqualTo(root.ToString()));
        });
    }

    [Test]
    public async Task Infoplanuser_without_a_plan_reports_no_limits_like_the_php_default_plan()
    {
        using var platform = await PlansHost.PlatformAsync();
        using var company = await PlansHost.CreateCompanyAsync(platform);

        using var response = await company.Client.GetAsync("/api/ubl2.1/plan/infoplanuser");

        using var json = await JsonAsync(response, HttpStatusCode.OK);
        var root = json.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("plan").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(root.GetProperty("period").GetString(), Is.EqualTo("SIN LIMITES - NO PLAN"));
            Assert.That(root.GetProperty("start_plan_date").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(root.GetProperty("renovation_date").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(root.GetProperty("docs_left_invoice").GetInt64(), Is.Zero);
        });
    }

    // En el PHP plan, plan/query y plan/queryusersbyplan eran públicas; aquí son de la plataforma. infoplanuser es de la empresa.
    [Test]
    public async Task Plan_administration_is_for_the_platform_and_infoplanuser_for_companies()
    {
        using var platform = await PlansHost.PlatformAsync();
        using var company = await PlansHost.CreateCompanyAsync(platform);
        using var anonymous = Api.CreateClient();
        var id = await CreatePlanAsync(platform);
        var routes = new (HttpMethod Method, string Path)[]
        {
            (HttpMethod.Put, "/api/ubl2.1/plan"), (HttpMethod.Get, "/api/ubl2.1/plan/query"), (HttpMethod.Get, $"/api/ubl2.1/plan/query/{id}"),
            (HttpMethod.Get, $"/api/ubl2.1/plan/queryusersbyplan/{id}"), (HttpMethod.Get, "/api/v1/plans"), (HttpMethod.Put, $"/api/v1/plans/{id}"),
            (HttpMethod.Get, $"/api/v1/plans/{id}/companies"), (HttpMethod.Put, $"/api/v1/companies/{company.Nit}/plan"),
        };

        foreach (var (method, path) in routes)
        {
            using var byCompany = await SendAsync(company.Client, method, path, Body(id, $"Plan {id}"));
            using var byNobody = await SendAsync(anonymous, method, path, Body(id, $"Plan {id}"));
            Assert.That(byCompany.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), $"{method} {path} with a company key");
            Assert.That(byNobody.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), $"{method} {path} without credential");
        }

        using var infoByPlatform = await platform.GetAsync("/api/ubl2.1/plan/infoplanuser");
        using var infoByNobody = await anonymous.GetAsync("/api/v1/plans/current");
        using var writeOnly = await Api.ClientWithNewKeyAsync(company.CompanyId, ApiScopes.DocumentsWrite);
        using var infoWithoutRead = await writeOnly.GetAsync("/api/ubl2.1/plan/infoplanuser");
        Assert.Multiple(() =>
        {
            Assert.That(infoByPlatform.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(infoByNobody.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(infoWithoutRead.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        });
    }

    private static int NextId() => Interlocked.Increment(ref planSequence);

    private static Dictionary<string, object?> Body(int id, string? name, int period = 3, long invoices = 100, long payroll = 0) => new()
    {
        ["id"] = id, ["name"] = name, ["qty_docs_invoice"] = invoices, ["qty_docs_payroll"] = payroll, ["qty_docs_radian"] = 0,
        ["qty_docs_ds"] = 0, ["period"] = period,
    };

    private static async Task<int> CreatePlanAsync(HttpClient platform, int period = 3, long invoices = 100, long payroll = 0)
    {
        var id = NextId();
        using var response = await platform.PutAsJsonAsync("/api/ubl2.1/plan", Body(id, $"Plan {id}", period, invoices, payroll));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return id;
    }

    private static async Task AddUsageAsync(Guid companyId, DocumentKind kind, long used)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var companyPlanId = await scope.Db.Set<CompanyPlan>().IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(row => row.CompanyId == companyId && row.IsActive).Select(row => row.Id).SingleAsync();
        scope.Db.Add(new PlanUsage { CompanyPlanId = companyPlanId, DocumentKind = kind, Used = used });
        await scope.Db.SaveChangesAsync();
    }

    /// <summary>El JSON compacto de un plan creado o cambiado a la hora fija de las pruebas.</summary>
    private static string PlanJson(int id, string name, long invoice, long payroll, long radian, long ds, int period, bool state, string? observations) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = id, ["name"] = name, ["qty_docs_invoice"] = invoice, ["qty_docs_payroll"] = payroll, ["qty_docs_radian"] = radian,
            ["qty_docs_ds"] = ds, ["period"] = period, ["state"] = state, ["observations"] = observations, ["created_at"] = NowLaravel,
            ["updated_at"] = NowLaravel,
        });

    private static IEnumerable<string> PropertyNames(JsonElement element) => element.EnumerateObject().Select(property => property.Name);

    private static async Task<JsonDocument> JsonAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(expected), text);
        return JsonDocument.Parse(text);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object body)
    {
        var request = new HttpRequestMessage(method, path);
        if (method == HttpMethod.Put)
        {
            request.Content = JsonContent.Create(body);
        }

        return client.SendAsync(request);
    }
}
