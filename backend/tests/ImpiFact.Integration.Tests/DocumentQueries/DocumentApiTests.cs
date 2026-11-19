using System.Net;
using System.Text.Json;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Integration.Tests.DocumentQueries;

/// <summary>Listado por clave, detalle y búsqueda por CUFE de <c>/api/v1/documents</c>, por la API y con PostgreSQL.</summary>
[Category("Integration")]
public sealed class DocumentApiTests
{
    private static readonly DateTimeOffset Start = new(2026, 2, 1, 15, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task List_walks_every_document_newest_first_with_the_cursor_and_never_shows_another_company()
    {
        var company = await DocumentSeed.CompanyAsync();
        var other = await DocumentSeed.CompanyAsync();
        var expected = new List<(DateTimeOffset IssuedAt, Guid Id)>();
        for (var number = 1; number <= 5; number++)
        {
            var at = Start.AddHours(number);
            expected.Add((at, await DocumentSeed.DocumentAsync(company, 1, number, DocumentStatus.Queued, at)));
        }

        // Dos documentos en el mismo instante: el id desempata el orden.
        expected.Add((Start.AddHours(3), await DocumentSeed.DocumentAsync(company, 4, 1, DocumentStatus.Accepted, Start.AddHours(3))));
        await DocumentSeed.DocumentAsync(other, 1, 99, DocumentStatus.Queued, Start.AddHours(10));
        using var client = await DocumentSeed.ClientAsync(company);

        var seen = new List<Guid>();
        string? cursor = null;
        var pages = 0;
        do
        {
            using var json = await DocumentSeed.GetJsonAsync(client, "/api/v1/documents?limit=2" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)));
            var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
            Assert.That(items, Has.Count.LessThanOrEqualTo(2));
            seen.AddRange(items.Select(item => item.GetProperty("id").GetGuid()));
            cursor = json.RootElement.GetProperty("next_cursor").ValueKind == JsonValueKind.Null ? null : json.RootElement.GetProperty("next_cursor").GetString();
            pages++;
        }
        while (cursor is not null && pages < 10);

        // PostgreSQL ordena los uuid por sus bytes, que es el orden del texto en hexadecimal.
        var newestFirst = expected.OrderByDescending(row => row.IssuedAt)
            .ThenByDescending(row => row.Id, TestDatabase.IdOrder)
            .Select(row => row.Id)
            .ToList();
        Assert.Multiple(() =>
        {
            Assert.That(seen, Is.EqualTo(newestFirst), "Every document once, newest first, none of the other company.");
            Assert.That(pages, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task List_filters_by_type_status_and_colombian_dates()
    {
        var company = await DocumentSeed.CompanyAsync();
        var march = new DateTimeOffset(2026, 3, 10, 5, 0, 0, TimeSpan.Zero); // 10-mar 00:00 en Colombia.
        var invoice = await DocumentSeed.DocumentAsync(company, 1, 1, DocumentStatus.Accepted, march);
        await DocumentSeed.DocumentAsync(company, 1, 2, DocumentStatus.Rejected, march);
        await DocumentSeed.DocumentAsync(company, 4, 1, DocumentStatus.Accepted, march);
        await DocumentSeed.DocumentAsync(company, 1, 3, DocumentStatus.Accepted, march.AddSeconds(-1)); // 9-mar en Colombia.
        await DocumentSeed.DocumentAsync(company, 1, 4, DocumentStatus.Accepted, march.AddDays(1)); // 11-mar.
        using var client = await DocumentSeed.ClientAsync(company);

        using var json = await DocumentSeed.GetJsonAsync(client, "/api/v1/documents?document_type_id=1&status=accepted&from=2026-03-10&to=2026-03-10");

        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(items.Select(item => item.GetProperty("id").GetGuid()), Is.EqualTo(new[] { invoice }));
            Assert.That(items[0].GetProperty("status").GetString(), Is.EqualTo("Accepted"));
            Assert.That(items[0].GetProperty("issue_date").GetString(), Is.EqualTo("2026-03-10"));
            Assert.That(items[0].GetProperty("issue_time").GetString(), Is.EqualTo("00:00:00"));
            Assert.That(items[0].GetProperty("full_number").GetString(), Is.EqualTo("SETP1"));
            Assert.That(items[0].GetProperty("customer").GetProperty("identification_number").GetString(), Is.EqualTo("222222222222"));
            Assert.That(items[0].GetProperty("totals").GetProperty("payable_amount").GetDecimal(), Is.EqualTo(119.00m));
            Assert.That(json.RootElement.GetProperty("next_cursor").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    [Test]
    public async Task Detail_by_id_and_by_cufe_return_the_header_lines_and_taxes()
    {
        var company = await DocumentSeed.CompanyAsync();
        var id = await DocumentSeed.DocumentAsync(company, 1, 990000010, DocumentStatus.Accepted, Start, cufe: "cufe-detail-" + company.Nit);
        await DocumentSeed.LineWithVatAsync(company, id);
        using var client = await DocumentSeed.ClientAsync(company);

        using var byId = await DocumentSeed.GetJsonAsync(client, $"/api/v1/documents/{id}");
        using var byCufe = await DocumentSeed.GetJsonAsync(client, $"/api/v1/documents/by-cufe/cufe-detail-{company.Nit}");

        var detail = byId.RootElement;
        var line = detail.GetProperty("lines")[0];
        Assert.Multiple(() =>
        {
            Assert.That(byCufe.RootElement.GetRawText(), Is.EqualTo(detail.GetRawText()));
            Assert.That(detail.GetProperty("id").GetGuid(), Is.EqualTo(id));
            Assert.That(detail.GetProperty("cufe").GetString(), Is.EqualTo("cufe-detail-" + company.Nit));
            Assert.That(detail.GetProperty("uuid_algorithm").GetString(), Is.EqualTo("CUFE-SHA384"));
            Assert.That(detail.GetProperty("status").GetString(), Is.EqualTo("Accepted"));
            Assert.That(detail.GetProperty("notes").GetString(), Is.EqualTo("nota de prueba"));
            Assert.That(detail.GetProperty("issued_at").GetDateTimeOffset(), Is.EqualTo(Start));
            Assert.That(detail.GetProperty("customer").GetProperty("email").GetString(), Is.EqualTo("cliente@example.com"));
            Assert.That(detail.GetProperty("totals").GetProperty("tax_inclusive_amount").GetDecimal(), Is.EqualTo(119.00m));
            Assert.That(detail.GetProperty("taxes")[0].GetProperty("tax_amount").GetDecimal(), Is.EqualTo(19.00m));
            Assert.That(line.GetProperty("description").GetString(), Is.EqualTo("Producto de prueba"));
            Assert.That(line.GetProperty("invoiced_quantity").GetDecimal(), Is.EqualTo(2m));
            Assert.That(line.GetProperty("price_amount").GetDecimal(), Is.EqualTo(50m));
            Assert.That(line.GetProperty("tax_totals")[0].GetProperty("percent").GetDecimal(), Is.EqualTo(19m));
        });
    }

    [Test]
    public async Task Another_companys_document_is_not_found_by_id_nor_by_cufe()
    {
        var company = await DocumentSeed.CompanyAsync();
        var other = await DocumentSeed.CompanyAsync();
        var foreign = await DocumentSeed.DocumentAsync(other, 1, 1, DocumentStatus.Accepted, Start, cufe: "cufe-foreign-" + other.Nit);
        using var client = await DocumentSeed.ClientAsync(company);

        using var byId = await client.GetAsync($"/api/v1/documents/{foreign}");
        using var byCufe = await client.GetAsync($"/api/v1/documents/by-cufe/cufe-foreign-{other.Nit}");
        using var missing = await client.GetAsync($"/api/v1/documents/{Guid.CreateVersion7()}");

        using var json = await DocumentSeed.JsonAsync(byId);
        Assert.Multiple(() =>
        {
            Assert.That(byId.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(byCufe.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(json.RootElement.GetProperty("code").GetString(), Is.EqualTo("document.not_found"));
        });
    }

    [Test]
    public async Task Canonical_routes_need_documents_read_and_reject_invalid_filters()
    {
        var company = await DocumentSeed.CompanyAsync();
        using var configOnly = await DocumentSeed.ClientAsync(company, ApiScopes.ConfigWrite);
        using var reader = await DocumentSeed.ClientAsync(company);

        using var list = await configOnly.GetAsync("/api/v1/documents");
        using var detail = await configOnly.GetAsync($"/api/v1/documents/{Guid.CreateVersion7()}");
        using var byCufe = await configOnly.GetAsync("/api/v1/documents/by-cufe/abc");
        using var invalid = await reader.GetAsync("/api/v1/documents?status=aprobado&limit=500");

        using var json = await DocumentSeed.JsonAsync(invalid);
        Assert.Multiple(() =>
        {
            Assert.That(list.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(detail.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(byCufe.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(json.RootElement.GetProperty("errors").GetProperty("status")[0].GetString(), Is.EqualTo("status es inválido."));
            Assert.That(json.RootElement.GetProperty("errors").GetProperty("limit")[0].GetString(), Is.EqualTo("El campo limit debe ser como máximo 200."));
        });
    }
}
