using System.Net;
using System.Text.Json;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Integration.Tests.DocumentQueries;

/// <summary>
/// Filtros del listado del panel (Arquitectura frontend, sección 8.3): familia, varios estados, adquiriente, número y
/// conteo total, siempre paginando en el servidor por la clave <c>(issued_at, id)</c>, por la API y con PostgreSQL.
/// </summary>
[Category("Integration")]
public sealed class DocumentListFilterApiTests
{
    private static readonly DateTimeOffset Start = new(2026, 4, 1, 15, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Kind_picks_the_document_type_and_every_row_says_its_kind_and_email_status()
    {
        var company = await DocumentSeed.CompanyAsync();
        await DocumentSeed.DocumentAsync(company, 1, 1, DocumentStatus.Accepted, Start);
        var credit = await DocumentSeed.DocumentAsync(company, 4, 1, DocumentStatus.Accepted, Start.AddMinutes(1), emailStatus: EmailStatus.Sent);
        await DocumentSeed.DocumentAsync(company, 5, 1, DocumentStatus.Accepted, Start.AddMinutes(2));
        await DocumentSeed.DocumentAsync(company, 11, 1, DocumentStatus.Accepted, Start.AddMinutes(3));
        using var client = await DocumentSeed.ClientAsync(company);

        using var credits = await DocumentSeed.GetJsonAsync(client, "/api/v1/documents?kind=credit_note");
        using var all = await DocumentSeed.GetJsonAsync(client, "/api/v1/documents");

        var item = credits.RootElement.GetProperty("items").EnumerateArray().Single();
        var kinds = all.RootElement.GetProperty("items").EnumerateArray()
            .Select(row => row.GetProperty("kind").ValueKind == JsonValueKind.Null ? null : row.GetProperty("kind").GetString())
            .ToList();
        Assert.Multiple(() =>
        {
            Assert.That(item.GetProperty("id").GetGuid(), Is.EqualTo(credit));
            Assert.That(item.GetProperty("kind").GetString(), Is.EqualTo("credit_note"));
            Assert.That(item.GetProperty("email_status").GetString(), Is.EqualTo("Sent"));
            Assert.That(kinds, Is.EqualTo(new[] { null, "debit_note", "credit_note", "invoice" }));
            Assert.That(all.RootElement.TryGetProperty("total_count", out _), Is.False, "total_count only comes when asked for.");
        });
    }

    [TestCase("status=accepted,rejected")]
    [TestCase("status=Accepted&status=Rejected")]
    public async Task Several_statuses_match_any_of_them(string query)
    {
        var company = await DocumentSeed.CompanyAsync();
        var accepted = await DocumentSeed.DocumentAsync(company, 1, 1, DocumentStatus.Accepted, Start);
        var rejected = await DocumentSeed.DocumentAsync(company, 1, 2, DocumentStatus.Rejected, Start.AddMinutes(1));
        await DocumentSeed.DocumentAsync(company, 1, 3, DocumentStatus.Queued, Start.AddMinutes(2));
        using var client = await DocumentSeed.ClientAsync(company);

        using var json = await DocumentSeed.GetJsonAsync(client, "/api/v1/documents?" + query);

        Assert.That(Ids(json), Is.EqualTo(new[] { rejected, accepted }));
    }

    [TestCase("acme", new[] { 2, 1 })]
    [TestCase("ACME s", new[] { 1 })]
    [TestCase("900123", new[] { 3, 1 })]
    [TestCase("%", new int[0])]
    [TestCase("acme_sas", new int[0])]
    public async Task Customer_matches_a_piece_of_the_name_or_the_identification_without_case(string customer, int[] expected)
    {
        var company = await DocumentSeed.CompanyAsync();
        var ids = new Dictionary<int, Guid>
        {
            [1] = await DocumentSeed.DocumentAsync(company, 1, 1, DocumentStatus.Accepted, Start, customerName: "Acme SAS", customerIdentification: "900123456"),
            [2] = await DocumentSeed.DocumentAsync(company, 1, 2, DocumentStatus.Accepted, Start.AddMinutes(1), customerName: "Tienda ACME"),
            [3] = await DocumentSeed.DocumentAsync(company, 1, 3, DocumentStatus.Accepted, Start.AddMinutes(2), customerIdentification: "8900123"),
        };
        await DocumentSeed.DocumentAsync(company, 1, 4, DocumentStatus.Accepted, Start.AddMinutes(3), customerName: "Otra empresa");
        using var client = await DocumentSeed.ClientAsync(company);

        using var json = await DocumentSeed.GetJsonAsync(client, "/api/v1/documents?customer=" + Uri.EscapeDataString(customer));

        Assert.That(Ids(json), Is.EqualTo(expected.Select(key => ids[key]).ToArray()));
    }

    [Test]
    public async Task Number_matches_the_full_number_or_only_the_digits_in_any_prefix()
    {
        var company = await DocumentSeed.CompanyAsync();
        var setp = await DocumentSeed.DocumentAsync(company, 1, 77, DocumentStatus.Accepted, Start);
        var fe = await DocumentSeed.DocumentAsync(company, 1, 77, DocumentStatus.Accepted, Start.AddMinutes(1), prefix: "FE");
        await DocumentSeed.DocumentAsync(company, 1, 770, DocumentStatus.Accepted, Start.AddMinutes(2));
        using var client = await DocumentSeed.ClientAsync(company);

        using var full = await DocumentSeed.GetJsonAsync(client, "/api/v1/documents?number=setp77");
        using var digits = await DocumentSeed.GetJsonAsync(client, "/api/v1/documents?number=77");

        Assert.Multiple(() =>
        {
            Assert.That(Ids(full), Is.EqualTo(new[] { setp }));
            Assert.That(Ids(digits), Is.EqualTo(new[] { fe, setp }));
        });
    }

    [Test]
    public async Task Include_total_counts_every_matching_row_on_every_page_and_never_another_companys()
    {
        var company = await DocumentSeed.CompanyAsync();
        var other = await DocumentSeed.CompanyAsync();
        for (var number = 1; number <= 5; number++)
        {
            await DocumentSeed.DocumentAsync(company, 1, number, DocumentStatus.Accepted, Start.AddMinutes(number));
        }

        await DocumentSeed.DocumentAsync(company, 1, 6, DocumentStatus.Rejected, Start.AddMinutes(6));
        await DocumentSeed.DocumentAsync(other, 1, 1, DocumentStatus.Accepted, Start);
        using var client = await DocumentSeed.ClientAsync(company);

        using var first = await DocumentSeed.GetJsonAsync(client, "/api/v1/documents?status=accepted&limit=2&include_total=true");
        var cursor = first.RootElement.GetProperty("next_cursor").GetString()!;
        using var second = await DocumentSeed.GetJsonAsync(
            client, "/api/v1/documents?status=accepted&limit=2&include_total=true&cursor=" + Uri.EscapeDataString(cursor));

        Assert.Multiple(() =>
        {
            Assert.That(first.RootElement.GetProperty("total_count").GetInt64(), Is.EqualTo(5));
            Assert.That(second.RootElement.GetProperty("total_count").GetInt64(), Is.EqualTo(5));
            Assert.That(Ids(first), Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task Pages_with_filters_and_identical_issue_instants_show_every_row_once_in_id_order()
    {
        var company = await DocumentSeed.CompanyAsync();
        var tied = new List<Guid>();
        for (var number = 1; number <= 5; number++)
        {
            tied.Add(await DocumentSeed.DocumentAsync(company, 1, number, DocumentStatus.Accepted, Start, customerName: "Cliente Empatado"));
        }

        await DocumentSeed.DocumentAsync(company, 1, 6, DocumentStatus.Accepted, Start, customerName: "Otro");
        using var client = await DocumentSeed.ClientAsync(company);

        var seen = new List<Guid>();
        string? cursor = null;
        var pages = 0;
        do
        {
            using var json = await DocumentSeed.GetJsonAsync(client,
                "/api/v1/documents?customer=empatado&limit=2" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)));
            seen.AddRange(Ids(json));
            var next = json.RootElement.GetProperty("next_cursor");
            cursor = next.ValueKind == JsonValueKind.Null ? null : next.GetString();
            pages++;
        }
        while (cursor is not null && pages < 10);

        // PostgreSQL ordena los uuid por sus bytes, que es el orden del texto en hexadecimal.
        Assert.That(seen, Is.EqualTo(tied.OrderByDescending(id => id, TestDatabase.IdOrder).ToList()));
    }

    [TestCase("kind=receipt", "kind", "kind es inválido.")]
    [TestCase("kind=invoice&document_type_id=4", "kind", "kind no coincide con document_type_id.")]
    [TestCase("status=accepted,,rejected", "status", "status es inválido.")]
    [TestCase("number=SETP-1", "number", "number es inválido.")]
    public async Task Invalid_filters_answer_422(string query, string field, string message)
    {
        var company = await DocumentSeed.CompanyAsync();
        using var client = await DocumentSeed.ClientAsync(company);

        using var response = await client.GetAsync("/api/v1/documents?" + query);

        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), body);
            Assert.That(json.RootElement.GetProperty("errors").GetProperty(field)[0].GetString(), Is.EqualTo(message));
        });
    }

    [Test]
    public async Task A_customer_text_longer_than_100_characters_answers_422()
    {
        var company = await DocumentSeed.CompanyAsync();
        using var client = await DocumentSeed.ClientAsync(company);

        using var response = await client.GetAsync("/api/v1/documents?customer=" + new string('a', 101));

        using var json = await DocumentSeed.JsonAsync(response);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(json.RootElement.GetProperty("errors").GetProperty("customer")[0].GetString(),
                Is.EqualTo("customer no debe ser mayor que 100 caracteres."));
        });
    }

    private static List<Guid> Ids(JsonDocument json) =>
        json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
}
