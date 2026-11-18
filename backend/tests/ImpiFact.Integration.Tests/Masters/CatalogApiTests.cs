using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using static ImpiFact.Integration.Tests.Masters.MastersHost;

namespace ImpiFact.Integration.Tests.Masters;

/// <summary>
/// Catálogos DIAN (Dirección de Impuestos y Aduanas Nacionales) de los formularios del panel, por la API y con los
/// catálogos sembrados por el sincronizador real: los cortos completos, los largos (municipios y unidades) por cursor,
/// búsqueda sin distinguir mayúsculas ni tildes, filas inactivas solo a pedido y respuestas que se pueden guardar en
/// caché con su ETag.
/// </summary>
[Category("Integration")]
public sealed class CatalogApiTests
{
    [Test]
    public async Task A_short_catalog_comes_whole_in_id_order_without_a_cursor()
    {
        using var client = await AnyClientAsync();

        var (status, body) = await SendAsync(client, HttpMethod.Get, "/api/v1/catalogs/identification-types");

        var items = body.GetProperty("items").EnumerateArray().ToList();
        var ids = items.Select(item => item.GetProperty("id").GetInt32()).ToList();
        var nit = items.Single(item => item.GetProperty("id").GetInt32() == 6);
        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(200), body.ToString());
            Assert.That(body.GetProperty("name").GetString(), Is.EqualTo("identification-types"));
            Assert.That(body.GetProperty("version").GetString(), Does.Match("^[0-9a-f]{32}$"));
            Assert.That(items, Has.Count.EqualTo(12));
            Assert.That(ids, Is.Ordered.Ascending);
            Assert.That(body.GetProperty("next_cursor").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(nit.GetProperty("code").GetString(), Is.EqualTo("31"));
            Assert.That(nit.GetProperty("name").GetString(), Is.EqualTo("NIT"));
            Assert.That(nit.GetProperty("is_active").GetBoolean(), Is.True);
        });
    }

    [TestCase("organization-types", 2)]
    [TestCase("regimes", 2)]
    [TestCase("liabilities", 5)]
    [TestCase("departments", 33)]
    [TestCase("countries", 249)]
    [TestCase("taxes", 23)]
    [TestCase("payment-forms", 2)]
    [TestCase("payment-methods", 75)]
    [TestCase("document-types", 14)]
    [TestCase("currencies", 179)]
    [TestCase("item-identification-types", 4)]
    [TestCase("credit-note-discrepancy-responses", 6)]
    [TestCase("debit-note-discrepancy-responses", 4)]
    public async Task Every_short_catalog_of_the_panel_forms_comes_whole(string name, int count)
    {
        using var client = await AnyClientAsync();

        var (status, body) = await SendAsync(client, HttpMethod.Get, $"/api/v1/catalogs/{name}");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(200), body.ToString());
            Assert.That(body.GetProperty("items").GetArrayLength(), Is.EqualTo(count));
            Assert.That(body.GetProperty("next_cursor").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    [TestCase("municipalities", 1122)]
    [TestCase("unit-measures", 1093)]
    public async Task A_long_catalog_is_paged_by_cursor_without_skipping_or_repeating_rows(string name, int count)
    {
        using var client = await AnyClientAsync();

        var (first, firstBody) = await SendAsync(client, HttpMethod.Get, $"/api/v1/catalogs/{name}");
        var (ids, pages) = await WalkIdsAsync(client, $"/api/v1/catalogs/{name}?limit=200");

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(200), firstBody.ToString());
            Assert.That(firstBody.GetProperty("items").GetArrayLength(), Is.EqualTo(50), "50 rows per page by default.");
            Assert.That(firstBody.GetProperty("next_cursor").GetString(), Is.Not.Empty);
            Assert.That(ids, Has.Count.EqualTo(count));
            Assert.That(ids, Is.Unique);
            Assert.That(ids, Is.Ordered.Ascending);
            Assert.That(pages, Is.EqualTo((count + 199) / 200));
        });
    }

    [Test]
    public async Task The_search_ignores_case_and_accents_and_also_matches_the_code()
    {
        using var client = await AnyClientAsync();

        var (_, bogota) = await SendAsync(client, HttpMethod.Get, "/api/v1/catalogs/municipalities?q=BOGOTA");
        var (_, medellin) = await SendAsync(client, HttpMethod.Get, "/api/v1/catalogs/municipalities?q=medellin");
        var (_, byCode) = await SendAsync(client, HttpMethod.Get, "/api/v1/catalogs/municipalities?q=76001");
        var (_, unit) = await SendAsync(client, HttpMethod.Get, "/api/v1/catalogs/unit-measures?q=unidad");
        var (_, nothing) = await SendAsync(client, HttpMethod.Get, "/api/v1/catalogs/municipalities?q=zzzz-no-existe");

        Assert.Multiple(() =>
        {
            Assert.That(Ids(bogota), Does.Contain(149));
            Assert.That(Names(bogota), Has.All.Contains("Bogot"));
            Assert.That(Ids(medellin), Does.Contain(1));
            Assert.That(Ids(byCode), Is.EqualTo(new[] { 1006 }));
            Assert.That(Ids(unit), Does.Contain(70));
            Assert.That(nothing.GetProperty("items").GetArrayLength(), Is.Zero);
            Assert.That(nothing.GetProperty("next_cursor").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    [Test]
    public async Task A_search_on_a_long_catalog_pages_its_matches()
    {
        using var client = await AnyClientAsync();

        var (all, _) = await WalkIdsAsync(client, "/api/v1/catalogs/municipalities?q=san&limit=200");
        var (paged, pages) = await WalkIdsAsync(client, "/api/v1/catalogs/municipalities?q=san&limit=10");

        Assert.Multiple(() =>
        {
            Assert.That(all, Has.Count.GreaterThan(10));
            Assert.That(paged, Is.EqualTo(all));
            Assert.That(pages, Is.GreaterThan(1));
        });
    }

    [Test]
    public async Task A_department_filters_the_municipalities_and_keeps_the_search_and_the_cursor()
    {
        using var client = await AnyClientAsync();

        var (all, _) = await WalkIdsAsync(client, "/api/v1/catalogs/municipalities?department_id=11&limit=200");
        var (paged, pages) = await WalkIdsAsync(client, "/api/v1/catalogs/municipalities?department_id=11&limit=10");
        var (status, first) = await SendAsync(client, HttpMethod.Get, "/api/v1/catalogs/municipalities?department_id=11");
        var (_, popayan) = await SendAsync(client, HttpMethod.Get, "/api/v1/catalogs/municipalities?department_id=11&q=popayan");
        var (_, elsewhere) = await SendAsync(client, HttpMethod.Get, "/api/v1/catalogs/municipalities?department_id=5&q=popayan");

        var departments = first.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("department_id").GetInt32());
        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(200), first.ToString());
            Assert.That(all, Has.Count.EqualTo(42), "Cauca has 42 municipalities in the seed.");
            Assert.That(all, Does.Contain(362));
            Assert.That(departments, Has.All.EqualTo(11));
            Assert.That(paged, Is.EqualTo(all));
            Assert.That(pages, Is.EqualTo(5));
            Assert.That(Ids(popayan), Is.EqualTo(new[] { 362 }));
            Assert.That(elsewhere.GetProperty("items").GetArrayLength(), Is.Zero);
        });
    }

    [TestCase("municipalities", "department_id=0")]
    [TestCase("municipalities", "department_id=abc")]
    [TestCase("unit-measures", "department_id=11")]
    [TestCase("departments", "department_id=11")]
    public async Task A_department_filter_that_does_not_apply_answers_422(string name, string query)
    {
        using var client = await AnyClientAsync();

        var (status, body) = await SendAsync(client, HttpMethod.Get, $"/api/v1/catalogs/{name}?{query}");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(422), body.ToString());
            Assert.That(body.GetProperty("errors").TryGetProperty("department_id", out _), Is.True, body.ToString());
        });
    }

    [Test]
    public async Task Catalog_rows_carry_their_parent_or_description()
    {
        using var client = await AnyClientAsync();

        var (_, municipalities) = await SendAsync(client, HttpMethod.Get, "/api/v1/catalogs/municipalities?q=11001");
        var (_, departments) = await SendAsync(client, HttpMethod.Get, "/api/v1/catalogs/departments");
        var (_, taxes) = await SendAsync(client, HttpMethod.Get, "/api/v1/catalogs/taxes");

        var bogota = municipalities.GetProperty("items")[0];
        var antioquia = departments.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetInt32() == 2);
        var iva = taxes.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetInt32() == 1);
        Assert.Multiple(() =>
        {
            Assert.That(bogota.GetProperty("department_id").GetInt32(), Is.EqualTo(5));
            Assert.That(antioquia.GetProperty("country_id").GetInt32(), Is.EqualTo(46));
            Assert.That(iva.GetProperty("description").GetString(), Is.EqualTo("Impuesto sobre la Ventas"));
            Assert.That(iva.TryGetProperty("department_id", out _), Is.False, "Only municipalities carry department_id.");
        });
    }

    [Test]
    public async Task An_inactive_row_is_left_out_unless_asked_for_and_changes_the_version()
    {
        using var client = await AnyClientAsync();
        var (_, before) = await SendAsync(client, HttpMethod.Get, "/api/v1/catalogs/payment-methods");
        await SetPaymentMethodActiveAsync(75, isActive: false);
        try
        {
            var (_, active) = await SendAsync(client, HttpMethod.Get, "/api/v1/catalogs/payment-methods");
            var (status, all) = await SendAsync(client, HttpMethod.Get, "/api/v1/catalogs/payment-methods?include_inactive=true");

            var inactive = all.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetInt32() == 75);
            Assert.Multiple(() =>
            {
                Assert.That(Ids(active), Does.Not.Contain(75));
                Assert.That(active.GetProperty("items").GetArrayLength(), Is.EqualTo(74));
                Assert.That(status, Is.EqualTo(200), all.ToString());
                Assert.That(all.GetProperty("items").GetArrayLength(), Is.EqualTo(75));
                Assert.That(inactive.GetProperty("is_active").GetBoolean(), Is.False);
                Assert.That(active.GetProperty("version").GetString(), Is.Not.EqualTo(before.GetProperty("version").GetString()));
            });
        }
        finally
        {
            await SetPaymentMethodActiveAsync(75, isActive: true);
        }
    }

    [Test]
    public async Task The_answer_is_cacheable_and_a_matching_etag_answers_304()
    {
        using var client = await AnyClientAsync();

        using var first = await client.GetAsync("/api/v1/catalogs/regimes");
        var etag = first.Headers.ETag;
        using var again = new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalogs/regimes");
        again.Headers.IfNoneMatch.Add(etag!);
        using var revalidated = await client.SendAsync(again);
        using var stale = new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalogs/regimes");
        stale.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"0123456789abcdef0123456789abcdef\""));
        using var refreshed = await client.SendAsync(stale);

        Assert.Multiple(async () =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(etag, Is.Not.Null);
            Assert.That(etag!.IsWeak, Is.False);
            Assert.That(first.Headers.CacheControl?.Public, Is.True);
            Assert.That(first.Headers.CacheControl?.MaxAge, Is.EqualTo(TimeSpan.FromHours(1)));
            Assert.That(revalidated.StatusCode, Is.EqualTo(HttpStatusCode.NotModified));
            Assert.That(revalidated.Headers.ETag, Is.EqualTo(etag));
            Assert.That(await revalidated.Content.ReadAsStringAsync(), Is.Empty);
            Assert.That(refreshed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });
    }

    [Test]
    public async Task Any_credential_reads_the_catalogs_and_an_anonymous_call_is_refused()
    {
        var company = await CompanyAsync();
        using var reader = await ClientAsync(company, ApiScopes.DocumentsRead);
        using var platform = await MastersHost.Api.ClientWithNewKeyAsync(null, ApiScopes.Platform);
        using var anonymous = MastersHost.Api.CreateClient();

        var (readerStatus, _) = await SendAsync(reader, HttpMethod.Get, "/api/v1/catalogs/taxes");
        var (platformStatus, _) = await SendAsync(platform, HttpMethod.Get, "/api/v1/catalogs/taxes");
        using var refused = await anonymous.GetAsync("/api/v1/catalogs/taxes");

        Assert.Multiple(() =>
        {
            Assert.That(readerStatus, Is.EqualTo(200));
            Assert.That(platformStatus, Is.EqualTo(200));
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        });
    }

    [Test]
    public async Task An_unknown_catalog_answers_404()
    {
        using var client = await AnyClientAsync();

        var (status, body) = await SendAsync(client, HttpMethod.Get, "/api/v1/catalogs/type_rejections");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(404));
            Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("catalog.not_found"));
            Assert.That(body.GetProperty("message").GetString(), Does.Contain("type_rejections"));
        });
    }

    [TestCase("limit=0", "limit")]
    [TestCase("limit=201", "limit")]
    [TestCase("cursor=no-es-un-cursor", "cursor")]
    [TestCase("q=%01", "q")]
    public async Task An_invalid_parameter_answers_422(string query, string field)
    {
        using var client = await AnyClientAsync();

        var (status, body) = await SendAsync(client, HttpMethod.Get, $"/api/v1/catalogs/municipalities?{query}");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(422), body.ToString());
            Assert.That(body.GetProperty("errors").TryGetProperty(field, out _), Is.True, body.ToString());
        });
    }

    [Test]
    public async Task A_search_longer_than_100_characters_answers_422()
    {
        using var client = await AnyClientAsync();

        var (status, body) = await SendAsync(client, HttpMethod.Get, $"/api/v1/catalogs/municipalities?q={new string('a', 101)}");

        Assert.That(status, Is.EqualTo(422), body.ToString());
    }

    private static async Task<HttpClient> AnyClientAsync() => await ClientAsync(await CompanyAsync(), ApiScopes.DocumentsRead);

    private static List<int> Ids(JsonElement body) =>
        body.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetInt32()).ToList();

    private static List<string> Names(JsonElement body) =>
        body.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()!).ToList();

    private static async Task<(List<int> Ids, int Pages)> WalkIdsAsync(HttpClient client, string url)
    {
        var ids = new List<int>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var (status, body) = await SendAsync(client, HttpMethod.Get, url + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)));
            Assert.That(status, Is.EqualTo(200), body.ToString());
            ids.AddRange(Ids(body));
            cursor = body.GetProperty("next_cursor").ValueKind == JsonValueKind.Null ? null : body.GetProperty("next_cursor").GetString();
            pages++;
        }
        while (cursor is not null && pages < 200);

        return (ids, pages);
    }

    private static async Task SetPaymentMethodActiveAsync(int id, bool isActive)
    {
        await using var scope = MastersHost.Api.OpenScope(null, null, ActorType.System);
        await scope.Db.Set<PaymentMethod>().Where(row => row.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsActive, isActive));
    }
}
