using System.Net;
using System.Text.Json;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Integration.Tests.DocumentQueries;

/// <summary>
/// <c>information</c> y <c>total_documents</c> de punta a punta, por la API y con PostgreSQL: forma heredada del PHP,
/// fechas por defecto, páginas, propiedad del NIT y alcances.
/// </summary>
[Category("Integration")]
public sealed class LegacyInformationApiTests
{
    private static readonly DateTimeOffset Jan15Noon = new(2026, 1, 15, 17, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Information_returns_the_php_shape_with_only_the_validated_documents_of_the_company()
    {
        var company = await DocumentSeed.CompanyAsync("EMPRESA INFORMACION SAS");
        var other = await DocumentSeed.CompanyAsync();
        var accepted = await DocumentSeed.DocumentAsync(company, 1, 990000001, DocumentStatus.Accepted, Jan15Noon, cufe: "cufe-info-1");
        await DocumentSeed.DocumentAsync(company, 1, 990000002, DocumentStatus.AcceptedWithWarnings, Jan15Noon.AddMinutes(1));
        await DocumentSeed.DocumentAsync(company, 1, 990000003, DocumentStatus.Rejected, Jan15Noon.AddMinutes(2));
        await DocumentSeed.DocumentAsync(company, 1, 990000004, DocumentStatus.Queued, Jan15Noon.AddMinutes(3));
        await DocumentSeed.DocumentAsync(company, 4, 1, DocumentStatus.Accepted, Jan15Noon);
        await DocumentSeed.DocumentAsync(company, 11, 1, DocumentStatus.Accepted, Jan15Noon);
        await DocumentSeed.DocumentAsync(other, 1, 990000001, DocumentStatus.Accepted, Jan15Noon);
        using var client = await DocumentSeed.ClientAsync(company);

        foreach (var url in new[] { $"/api/information/{company.Nit}", $"/api/v1/information/{company.Nit}" })
        {
            using var json = await DocumentSeed.GetJsonAsync(client, url);
            var root = json.RootElement;
            var data = root.GetProperty("data").EnumerateArray().ToList();
            var invoice = data[0].GetProperty("documents")[0];
            Assert.Multiple(() =>
            {
                Assert.That(root.GetProperty("success").GetBoolean(), Is.True, url);
                Assert.That(root.GetProperty("message").GetString(), Is.EqualTo("NIT Encontrado"));
                Assert.That(root.GetProperty("company").GetString(), Is.EqualTo("EMPRESA INFORMACION SAS"));
                Assert.That(data.Select(group => group.GetProperty("type_document_id").GetInt32()), Is.EqualTo(new[] { 1, 4, 5, 15 }));
                Assert.That(data.Select(group => group.GetProperty("name").GetString()),
                    Is.EqualTo(new[] { "Factura de Venta Nacional", "Nota Credito", "Nota Debito", "Documento Equivalente POS" }));
                Assert.That(data.Select(group => group.GetProperty("count").GetInt32()), Is.EqualTo(new[] { 2, 1, 0, 0 }));
                Assert.That(data.Select(group => group.GetProperty("documents").GetArrayLength()), Is.EqualTo(new[] { 2, 1, 0, 0 }));
                Assert.That(invoice.GetProperty("id").GetGuid(), Is.EqualTo(accepted));
                Assert.That(invoice.GetProperty("identification_number").GetString(), Is.EqualTo(company.Nit));
                Assert.That(invoice.GetProperty("state_document_id").GetInt32(), Is.EqualTo(1));
                Assert.That(invoice.GetProperty("type_document_id").GetInt32(), Is.EqualTo(1));
                Assert.That(invoice.GetProperty("prefix").GetString(), Is.EqualTo("SETP"));
                Assert.That(invoice.GetProperty("number").GetInt64(), Is.EqualTo(990000001));
                Assert.That(invoice.GetProperty("cufe").GetString(), Is.EqualTo("cufe-info-1"));
                Assert.That(invoice.GetProperty("customer").GetString(), Is.EqualTo("222222222222"));
                Assert.That(invoice.GetProperty("client").GetProperty("name").GetString(), Is.EqualTo("Consumidor final"));
                Assert.That(invoice.GetProperty("currency_id").GetInt32(), Is.EqualTo(35));
                Assert.That(invoice.GetProperty("date_issue").GetString(), Is.EqualTo("2026-01-15 12:00:00"));
                Assert.That(invoice.GetProperty("total").GetDecimal(), Is.EqualTo(119.00m));
                Assert.That(invoice.GetProperty("total_tax").GetDecimal(), Is.EqualTo(19.00m));
                Assert.That(invoice.GetProperty("subtotal").GetDecimal(), Is.EqualTo(100.00m));
                Assert.That(invoice.GetProperty("ambient_id").GetInt32(), Is.EqualTo(2));
            });
        }
    }

    [Test]
    public async Task One_date_covers_that_colombian_day_and_two_dates_cover_the_whole_range()
    {
        var company = await DocumentSeed.CompanyAsync();
        var day = new DateTimeOffset(2026, 3, 10, 5, 0, 0, TimeSpan.Zero); // 10-mar 00:00 en Colombia.
        await DocumentSeed.DocumentAsync(company, 1, 1, DocumentStatus.Accepted, day.AddTicks(-10)); // 9-mar 23:59:59 en Colombia.
        await DocumentSeed.DocumentAsync(company, 1, 2, DocumentStatus.Accepted, day);
        await DocumentSeed.DocumentAsync(company, 1, 3, DocumentStatus.Accepted, day.AddDays(1).AddSeconds(-1)); // 10-mar 23:59:59.
        await DocumentSeed.DocumentAsync(company, 1, 4, DocumentStatus.Accepted, day.AddDays(1)); // 11-mar 00:00.
        using var client = await DocumentSeed.ClientAsync(company);

        Assert.Multiple(async () =>
        {
            Assert.That(await InvoiceNumbers(client, $"/api/information/{company.Nit}/2026-03-10"), Is.EqualTo(new long[] { 2, 3 }));
            Assert.That(await InvoiceNumbers(client, $"/api/information/{company.Nit}/2026-03-09/2026-03-11"), Is.EqualTo(new long[] { 1, 2, 3, 4 }));
            Assert.That(await InvoiceNumbers(client, $"/api/information/{company.Nit}"), Is.EqualTo(new long[] { 1, 2, 3, 4 }), "1900-01-01 to 2100-01-01.");
            Assert.That(await InvoiceNumbers(client, $"/api/information/{company.Nit}/2026-03-11/2026-03-10"), Is.Empty);
        });
    }

    [Test]
    public async Task Information_by_page_returns_one_hundred_per_type_from_the_oldest()
    {
        var company = await DocumentSeed.CompanyAsync();
        var first = new DateTimeOffset(2025, 6, 1, 15, 0, 0, TimeSpan.Zero);
        for (var number = 1; number <= 101; number++)
        {
            await DocumentSeed.DocumentAsync(company, 1, number, DocumentStatus.Accepted, first.AddMinutes(number));
        }

        await DocumentSeed.DocumentAsync(company, 5, 1, DocumentStatus.Accepted, first);
        await DocumentSeed.DocumentAsync(company, 5, 2, DocumentStatus.Failed, first);
        using var client = await DocumentSeed.ClientAsync(company);

        using var page1 = await DocumentSeed.GetJsonAsync(client, $"/api/information/{company.Nit}/page/1/page");
        using var page2 = await DocumentSeed.GetJsonAsync(client, $"/api/v1/information/{company.Nit}/page/2/page");
        using var page0 = await DocumentSeed.GetJsonAsync(client, $"/api/information/{company.Nit}/page/0/page");

        var firstPage = Numbers(page1.RootElement.GetProperty("data")[0]);
        Assert.Multiple(() =>
        {
            Assert.That(firstPage, Is.EqualTo(Enumerable.Range(1, 100).Select(number => (long)number)));
            Assert.That(Numbers(page2.RootElement.GetProperty("data")[0]), Is.EqualTo(new long[] { 101 }));
            Assert.That(Numbers(page0.RootElement.GetProperty("data")[0]), Is.EqualTo(firstPage), "A page below 1 is the first page.");
            Assert.That(page1.RootElement.GetProperty("data").EnumerateArray().Select(group => group.GetProperty("count").GetInt32()),
                Is.EqualTo(new[] { 100, 0, 1, 0 }));
            Assert.That(page1.RootElement.GetProperty("data").EnumerateArray().Select(group => group.TryGetProperty("type_document_id", out _)),
                Is.All.False, "The PHP page variant has no type_document_id.");
            Assert.That(page1.RootElement.GetProperty("message").GetString(), Is.EqualTo("NIT Encontrado"));
        });
    }

    [Test]
    public async Task Total_documents_counts_the_eight_php_types_in_the_range()
    {
        var company = await DocumentSeed.CompanyAsync("EMPRESA TOTALES SAS");
        var other = await DocumentSeed.CompanyAsync();
        var january = new DateTimeOffset(2026, 1, 20, 15, 0, 0, TimeSpan.Zero);
        await DocumentSeed.DocumentAsync(company, 1, 1, DocumentStatus.Accepted, january);
        await DocumentSeed.DocumentAsync(company, 1, 2, DocumentStatus.AcceptedWithWarnings, january);
        await DocumentSeed.DocumentAsync(company, 1, 3, DocumentStatus.Rejected, january);
        await DocumentSeed.DocumentAsync(company, 1, 4, DocumentStatus.Accepted, january.AddMonths(1));
        await DocumentSeed.DocumentAsync(company, 4, 1, DocumentStatus.Accepted, january);
        await DocumentSeed.DocumentAsync(company, 11, 1, DocumentStatus.Accepted, january);
        await DocumentSeed.DocumentAsync(company, 13, 1, DocumentStatus.Accepted, january);
        await DocumentSeed.DocumentAsync(other, 1, 1, DocumentStatus.Accepted, january);
        using var client = await DocumentSeed.ClientAsync(company);

        using var january2026 = await DocumentSeed.GetJsonAsync(client, $"/api/total_documents/{company.Nit}/2026-01-01/2026-01-31");
        using var everything = await DocumentSeed.GetJsonAsync(client, $"/api/v1/total_documents/{company.Nit}");

        var data = january2026.RootElement.GetProperty("data").EnumerateArray().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(january2026.RootElement.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(january2026.RootElement.GetProperty("message").GetString(), Is.EqualTo("NIT Encontrado"));
            Assert.That(january2026.RootElement.GetProperty("company").GetString(), Is.EqualTo("EMPRESA TOTALES SAS"));
            Assert.That(data.Select(total => total.GetProperty("name").GetString()), Is.EqualTo(new[]
            {
                "Factura de Venta Nacional", "Nota Credito", "Nota Debito", "Documento Soporte a No Obligados a Facturar",
                "Notas de Ajuste al Documento Soporte a No Obligados a Facturar", "Documento Equivalente POS",
                "Nota Credito a Documento POS", "Nota Debito a Documento POS",
            }));
            Assert.That(data.Select(total => total.GetProperty("count").GetInt64()), Is.EqualTo(new long[] { 2, 1, 0, 1, 1, 0, 0, 0 }));
            Assert.That(everything.RootElement.GetProperty("data")[0].GetProperty("count").GetInt64(), Is.EqualTo(3));
        });
    }

    [Test]
    public async Task Another_nit_is_forbidden_with_the_same_answer_whether_it_exists_or_not()
    {
        var company = await DocumentSeed.CompanyAsync();
        var other = await DocumentSeed.CompanyAsync();
        await DocumentSeed.DocumentAsync(other, 1, 1, DocumentStatus.Accepted, Jan15Noon);
        using var client = await DocumentSeed.ClientAsync(company);
        const string missing = "811999999";

        foreach (var route in new[] { "/api/information/{0}", "/api/information/{0}/2026-01-15", "/api/information/{0}/page/1/page", "/api/total_documents/{0}", "/api/v1/total_documents/{0}/2026-01-01/2026-01-31" })
        {
            var (existingStatus, existingBody) = await ErrorOf(client, string.Format(System.Globalization.CultureInfo.InvariantCulture, route, other.Nit));
            var (missingStatus, missingBody) = await ErrorOf(client, string.Format(System.Globalization.CultureInfo.InvariantCulture, route, missing));
            Assert.Multiple(() =>
            {
                Assert.That(existingStatus, Is.EqualTo(HttpStatusCode.Forbidden), route);
                Assert.That(missingStatus, Is.EqualTo(HttpStatusCode.Forbidden), route);
                Assert.That(existingBody, Is.EqualTo(missingBody), route);
                Assert.That(existingBody, Does.Not.Contain(other.Name));
            });
        }
    }

    [Test]
    public async Task The_routes_need_a_credential_with_documents_read()
    {
        var company = await DocumentSeed.CompanyAsync();
        using var configOnly = await DocumentSeed.ClientAsync(company, ApiScopes.ConfigWrite);
        using var anonymous = DocumentQueriesHost.Api.CreateClient();

        foreach (var url in new[] { $"/api/information/{company.Nit}", $"/api/information/{company.Nit}/page/1/page", $"/api/total_documents/{company.Nit}" })
        {
            using var forbidden = await configOnly.GetAsync(url);
            using var unauthorized = await anonymous.GetAsync(url);
            Assert.Multiple(() =>
            {
                Assert.That(forbidden.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), url);
                Assert.That(unauthorized.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), url);
            });
        }
    }

    [Test]
    public async Task Invalid_dates_and_pages_answer_422_with_the_field()
    {
        var company = await DocumentSeed.CompanyAsync();
        using var client = await DocumentSeed.ClientAsync(company);

        using var badDate = await client.GetAsync($"/api/total_documents/{company.Nit}/2026-13-01");
        using var badPage = await client.GetAsync($"/api/information/{company.Nit}/page/uno/page");

        using var dateJson = await DocumentSeed.JsonAsync(badDate);
        using var pageJson = await DocumentSeed.JsonAsync(badPage);
        Assert.Multiple(() =>
        {
            Assert.That(badDate.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(dateJson.RootElement.GetProperty("errors").GetProperty("desde")[0].GetString(), Is.EqualTo("desde no es una fecha válida."));
            Assert.That(dateJson.RootElement.GetProperty("success").GetBoolean(), Is.False);
            Assert.That(badPage.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(pageJson.RootElement.GetProperty("errors").GetProperty("page")[0].GetString(), Is.EqualTo("page debe ser un número entero."));
        });
    }

    private static async Task<long[]> InvoiceNumbers(HttpClient client, string url)
    {
        using var json = await DocumentSeed.GetJsonAsync(client, url);
        return Numbers(json.RootElement.GetProperty("data")[0]);
    }

    private static long[] Numbers(JsonElement group) =>
        group.GetProperty("documents").EnumerateArray().Select(document => document.GetProperty("number").GetInt64()).ToArray();

    /// <summary>Código HTTP y cuerpo sin los campos que cambian en cada solicitud (traceId, correlation_id).</summary>
    private static async Task<(HttpStatusCode Status, string Body)> ErrorOf(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        using var json = await DocumentSeed.JsonAsync(response);
        var stable = json.RootElement.EnumerateObject()
            .Where(property => property.Name is not ("traceId" or "correlation_id" or "instance"))
            .Select(property => $"{property.Name}={property.Value.GetRawText()}");
        return (response.StatusCode, string.Join(";", stable));
    }
}
