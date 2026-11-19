using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using static ImpiFact.Integration.Tests.Companies.CompanyApiTests;

namespace ImpiFact.Integration.Tests.Companies;

/// <summary>
/// <c>GET /api/v1/platform/companies</c> (Arquitectura frontend, sección 16, punto 14): las empresas de la plataforma
/// para <c>/plataforma/empresas</c>, ordenadas por razón social, con búsqueda por NIT o nombre, cursor y el nombre del
/// plan vigente. Solo para credenciales de plataforma.
/// </summary>
[Category("Integration")]
public sealed class PlatformCompanyListTests
{
    private static CompanyApiFactory Api => CompaniesHost.Api;

    [Test]
    public async Task Search_by_name_lists_companies_by_name_with_their_current_plan_and_pages_by_cursor()
    {
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6));
        using var platform = await Api.ClientWithNewKeyAsync(null, ApiScopes.Platform);
        var zeta = await CreateAsync(platform, $"ZETA {token} S.A.S.");
        var alfa = await CreateAsync(platform, $"alfa {token} ltda");
        var beta = await CreateAsync(platform, $"BETA {token} S.A.S.");
        var planId = RandomNumberGenerator.GetInt32(500, 1000);
        using (var plan = await platform.PutAsJsonAsync($"/api/v1/plans/{planId}", new { name = $"Plan {token}", qty_docs_invoice = 100, qty_docs_payroll = 0, qty_docs_radian = 0, qty_docs_ds = 0, period = 1 }))
        {
            Assert.That(plan.StatusCode, Is.EqualTo(HttpStatusCode.OK), await plan.Content.ReadAsStringAsync());
        }

        using (var assigned = await platform.PutAsJsonAsync($"/api/v1/companies/{beta.Nit}/plan", new { plan_id = planId }))
        {
            Assert.That(assigned.StatusCode, Is.EqualTo(HttpStatusCode.OK), await assigned.Content.ReadAsStringAsync());
        }

        using var firstPage = await platform.GetAsync($"/api/v1/platform/companies?search={token.ToUpperInvariant()}&limit=2");
        var firstBody = await firstPage.Content.ReadAsStringAsync();
        Assert.That(firstPage.StatusCode, Is.EqualTo(HttpStatusCode.OK), firstBody);
        var page1 = JsonDocument.Parse(firstBody).RootElement;
        var cursor = page1.GetProperty("next_cursor").GetString()!;
        using var secondPage = await platform.GetAsync($"/api/v1/platform/companies?search={token}&limit=2&cursor={Uri.EscapeDataString(cursor)}");
        var page2 = JsonDocument.Parse(await secondPage.Content.ReadAsStringAsync()).RootElement;

        var items = page1.GetProperty("items").EnumerateArray().Concat(page2.GetProperty("items").EnumerateArray()).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(secondPage.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(items.Select(item => item.GetProperty("nit").GetString()), Is.EqualTo(new[] { alfa.Nit, beta.Nit, zeta.Nit }),
                "Ordered by name without minding capitals; the search ignores capitals too.");
            Assert.That(page2.GetProperty("next_cursor").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(items[0].GetProperty("name").GetString(), Is.EqualTo($"alfa {token} ltda"));
            Assert.That(items[0].GetProperty("id").GetGuid(), Is.EqualTo(alfa.Id));
            Assert.That(items[0].GetProperty("dv").GetString(), Is.EqualTo(alfa.Dv));
            Assert.That(items[0].GetProperty("email").GetString(), Is.EqualTo($"{alfa.Nit}@example.com"));
            Assert.That(items[0].GetProperty("is_active").GetBoolean(), Is.True);
            Assert.That(items[0].GetProperty("plan_id").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(items[0].GetProperty("plan_name").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(items[1].GetProperty("plan_id").GetInt32(), Is.EqualTo(planId));
            Assert.That(items[1].GetProperty("plan_name").GetString(), Is.EqualTo($"Plan {token}"));
        });
    }

    [Test]
    public async Task Search_by_nit_finds_the_company()
    {
        using var platform = await Api.ClientWithNewKeyAsync(null, ApiScopes.Platform);
        var created = await CreateAsync(platform, "EMPRESA BUSCADA POR NIT");

        using var json = await Json(await platform.GetAsync($"/api/v1/platform/companies?search={created.Nit}"));

        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.That(items.Select(item => item.GetProperty("id").GetGuid()), Is.EqualTo(new[] { created.Id }));
    }

    [Test]
    public async Task Only_platform_credentials_list_the_platform_companies()
    {
        var (company, _) = await CreateCompanyAsync();

        using var response = await company.GetAsync("/api/v1/platform/companies");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task A_wrong_cursor_limit_or_search_answers_422()
    {
        using var platform = await Api.ClientWithNewKeyAsync(null, ApiScopes.Platform);

        using var badCursor = await platform.GetAsync("/api/v1/platform/companies?cursor=no-es-un-cursor");
        using var badLimit = await platform.GetAsync("/api/v1/platform/companies?limit=0");
        using var badSearch = await platform.GetAsync($"/api/v1/platform/companies?search={new string('a', 101)}");
        using var cursorJson = await Json(badCursor);
        using var limitJson = await Json(badLimit);
        using var searchJson = await Json(badSearch);

        Assert.Multiple(() =>
        {
            Assert.That(badCursor.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(cursorJson.RootElement.GetProperty("errors").GetProperty("cursor")[0].GetString(), Is.EqualTo("cursor es inválido."));
            Assert.That(badLimit.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(limitJson.RootElement.GetProperty("errors").GetProperty("limit")[0].GetString(), Is.EqualTo("El campo limit debe ser como mínimo 1."));
            Assert.That(badSearch.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(searchJson.RootElement.GetProperty("errors").GetProperty("search")[0].GetString(),
                Is.EqualTo("search no debe ser mayor que 100 caracteres."));
        });
    }

    private static async Task<(Guid Id, string Nit, string Dv)> CreateAsync(HttpClient platform, string name)
    {
        var (nit, dv) = CompaniesHost.NewNit();
        using var response = await platform.PostAsJsonAsync($"/api/v1/companies/{nit}/{dv}", CompanyBody(nit, businessName: name));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        using var json = await Json(response);
        return (json.RootElement.GetProperty("company").GetProperty("id").GetGuid(), nit, dv);
    }
}
