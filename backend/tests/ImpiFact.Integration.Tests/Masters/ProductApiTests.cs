using System.Text.Json;
using System.Text.Json.Serialization;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Masters;
using Microsoft.EntityFrameworkCore;
using static ImpiFact.Integration.Tests.Masters.MastersHost;

namespace ImpiFact.Integration.Tests.Masters;

/// <summary>
/// Productos de la empresa por la API y con PostgreSQL: alta y edición con las reglas de impuestos de la emisión, código
/// único por empresa, desactivación sin borrar, aislamiento por empresa y listado paginado y filtrado en el servidor.
/// </summary>
[Category("Integration")]
public sealed class ProductApiTests
{
    [Test]
    public async Task Create_answers_201_and_stores_the_price_and_the_tax_rate()
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company);

        var (status, created) = await SendAsync(client, HttpMethod.Post, "/api/v1/products", Product("P-1") with
        {
            Description = "Soporte remoto", UnitPrice = 150000.123456m, TaxId = 1, TaxPercent = 19m,
        });
        var id = created.GetProperty("id").GetGuid();
        var (_, fetched) = await SendAsync(client, HttpMethod.Get, $"/api/v1/products/{id}");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(201), created.ToString());
            // Los números salen con la escala de la columna al leerlos (19.0000): se comparan por valor, no por texto.
            Assert.That(fetched.GetProperty("id").GetGuid(), Is.EqualTo(id));
            Assert.That(created.GetProperty("tax_percent").GetDecimal(), Is.EqualTo(19m));
            Assert.That(fetched.GetProperty("code").GetString(), Is.EqualTo("P-1"));
            Assert.That(fetched.GetProperty("name").GetString(), Is.EqualTo("Producto P-1"));
            Assert.That(fetched.GetProperty("description").GetString(), Is.EqualTo("Soporte remoto"));
            Assert.That(fetched.GetProperty("unit_measure_id").GetInt32(), Is.EqualTo(70));
            Assert.That(fetched.GetProperty("type_item_identification_id").GetInt32(), Is.EqualTo(4), "The company's own code standard by default.");
            Assert.That(fetched.GetProperty("unit_price").GetDecimal(), Is.EqualTo(150000.123456m));
            Assert.That(fetched.GetProperty("tax_id").GetInt32(), Is.EqualTo(1));
            Assert.That(fetched.GetProperty("tax_percent").GetDecimal(), Is.EqualTo(19m));
            Assert.That(fetched.GetProperty("tax_per_unit_amount").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(fetched.GetProperty("is_active").GetBoolean(), Is.True);
        });
        var stored = await StoredAsync(company, id);
        Assert.That((stored.SaleUnitPrice, stored.TaxId, stored.TaxPercent), Is.EqualTo((150000.123456m, (int?)1, (decimal?)19m)));
    }

    [Test]
    public async Task The_tax_rules_of_the_issue_flow_apply()
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company);

        var (noPercent, missing) = await SendAsync(client, HttpMethod.Post, "/api/v1/products", Product("T-1") with { TaxId = 1 });
        var (bags, bagsBody) = await SendAsync(client, HttpMethod.Post, "/api/v1/products", Product("T-2") with { TaxId = 10 });
        var (bagsOk, _) = await SendAsync(client, HttpMethod.Post, "/api/v1/products", Product("T-3") with { TaxId = 10, TaxPerUnitAmount = 66m });
        var (orphan, orphanBody) = await SendAsync(client, HttpMethod.Post, "/api/v1/products", Product("T-4") with { TaxPercent = 19m });
        var (bad, badBody) = await SendAsync(client, HttpMethod.Post, "/api/v1/products", new ProductBody
        {
            UnitPrice = -1m, TaxId = 999, TaxPercent = 120m,
        });
        var (scale, scaleBody) = await SendAsync(client, HttpMethod.Post, "/api/v1/products", Product("T-5") with { UnitPrice = 1.1234567m });

        Assert.Multiple(() =>
        {
            Assert.That(noPercent, Is.EqualTo(422));
            Assert.That(Field(missing, "tax_percent"), Is.EqualTo("El campo tax percent es obligatorio a menos que tax id esté en 10, 2."));
            Assert.That(bags, Is.EqualTo(422));
            Assert.That(Field(bagsBody, "tax_per_unit_amount"), Is.EqualTo("El campo tax per unit amount es obligatorio cuando tax id es 10."));
            Assert.That(Field(bagsBody, "tax_percent"), Is.Null, "The plastic bag tax has no percent.");
            Assert.That(bagsOk, Is.EqualTo(201));
            Assert.That(orphan, Is.EqualTo(422));
            Assert.That(Field(orphanBody, "tax_percent"), Is.EqualTo("El campo tax percent solo se permite con tax id."));
            Assert.That(bad, Is.EqualTo(422));
            Assert.That(Field(badBody, "code"), Is.EqualTo("El campo code es obligatorio."));
            Assert.That(Field(badBody, "name"), Is.EqualTo("El campo name es obligatorio."));
            Assert.That(Field(badBody, "unit_measure_id"), Is.EqualTo("El campo unit measure id es obligatorio."));
            Assert.That(Field(badBody, "unit_price"), Is.EqualTo("unit price es inválido."));
            Assert.That(Field(badBody, "tax_percent"), Is.EqualTo("tax percent es inválido."));
            Assert.That(scale, Is.EqualTo(422));
            Assert.That(Field(scaleBody, "unit_price"), Is.EqualTo("unit price admite como máximo 6 decimales."));
        });
    }

    [Test]
    public async Task An_unknown_catalog_id_is_a_422()
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company);

        var (status, body) = await SendAsync(client, HttpMethod.Post, "/api/v1/products", Product("C-1") with
        {
            UnitMeasureId = 99_999, TypeItemIdentificationId = 77, TaxId = 999, TaxPercent = 5m,
        });

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(422));
            Assert.That(Field(body, "unit_measure_id"), Is.EqualTo("unit measure id es inválido."));
            Assert.That(Field(body, "type_item_identification_id"), Is.EqualTo("type item identification id es inválido."));
            Assert.That(Field(body, "tax_id"), Is.EqualTo("tax id es inválido."));
        });
    }

    [Test]
    public async Task The_code_is_unique_per_company_even_when_deactivated()
    {
        var company = await CompanyAsync();
        var other = await CompanyAsync();
        using var client = await ClientAsync(company);
        using var otherClient = await ClientAsync(other);
        var first = await CreateAsync(client, Product("DUP"));
        var second = await CreateAsync(client, Product("OTHER"));
        await SendAsync(client, HttpMethod.Post, $"/api/v1/products/{first}/deactivate");

        var (again, body) = await SendAsync(client, HttpMethod.Post, "/api/v1/products", Product("DUP"));
        var (rename, renameBody) = await SendAsync(client, HttpMethod.Put, $"/api/v1/products/{second}", Product("DUP"));
        var (otherCompany, _) = await SendAsync(otherClient, HttpMethod.Post, "/api/v1/products", Product("DUP"));

        Assert.Multiple(() =>
        {
            Assert.That(again, Is.EqualTo(409));
            Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("product.duplicate"));
            Assert.That(rename, Is.EqualTo(409));
            Assert.That(renameBody.GetProperty("code").GetString(), Is.EqualTo("product.duplicate"));
            Assert.That(otherCompany, Is.EqualTo(201));
        });
    }

    [Test]
    public async Task Update_replaces_the_fields_and_deactivate_then_reactivate_keeps_the_row()
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company);
        var id = await CreateAsync(client, Product("U-1") with { Description = "Viejo", TaxId = 1, TaxPercent = 19m });

        var (deactivated, _) = await SendAsync(client, HttpMethod.Post, $"/api/v1/products/{id}/deactivate");
        var (_, active) = await SendAsync(client, HttpMethod.Get, "/api/v1/products");
        var (status, updated) = await SendAsync(client, HttpMethod.Put, $"/api/v1/products/{id}", new ProductUpdate
        {
            Code = "U-1", Name = "Nuevo nombre", UnitMeasureId = 70, UnitPrice = 10m, IsActive = true,
        });

        Assert.Multiple(() =>
        {
            Assert.That(deactivated, Is.EqualTo(204));
            Assert.That(Codes(active), Does.Not.Contain("U-1"));
            Assert.That(status, Is.EqualTo(200), updated.ToString());
            Assert.That(updated.GetProperty("name").GetString(), Is.EqualTo("Nuevo nombre"));
            Assert.That(updated.GetProperty("description").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(updated.GetProperty("tax_id").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(updated.GetProperty("is_active").GetBoolean(), Is.True);
        });
        var stored = await StoredAsync(company, id);
        Assert.That((stored.Name, stored.TaxPercent, stored.IsActive, stored.DeletedAt), Is.EqualTo(("Nuevo nombre", (decimal?)null, true, (DateTimeOffset?)null)));
    }

    [Test]
    public async Task Another_companys_product_is_not_found_and_never_listed()
    {
        var company = await CompanyAsync();
        var other = await CompanyAsync();
        using var client = await ClientAsync(company);
        using var otherClient = await ClientAsync(other);
        var foreign = await CreateAsync(otherClient, Product("FOREIGN"));

        var (get, _) = await SendAsync(client, HttpMethod.Get, $"/api/v1/products/{foreign}");
        var (put, _) = await SendAsync(client, HttpMethod.Put, $"/api/v1/products/{foreign}", Product("FOREIGN"));
        var (deactivate, body) = await SendAsync(client, HttpMethod.Post, $"/api/v1/products/{foreign}/deactivate");
        var (_, list) = await SendAsync(client, HttpMethod.Get, "/api/v1/products?status=all");

        Assert.Multiple(() =>
        {
            Assert.That(get, Is.EqualTo(404));
            Assert.That(put, Is.EqualTo(404));
            Assert.That(deactivate, Is.EqualTo(404));
            Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("product.not_found"));
            Assert.That(Codes(list), Is.Empty);
        });
        Assert.That((await StoredAsync(other, foreign)).IsActive, Is.True);
    }

    [Test]
    public async Task Search_by_code_or_name_and_walk_the_pages_by_code()
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company);
        string[] codes = ["B-02", "A-01", "C-03", "A-10"];
        foreach (var code in codes)
        {
            await CreateAsync(client, Product(code) with { Name = code.StartsWith('A') ? "Tornillo " + code : "Arandela " + code });
        }

        var byCode = await WalkAsync(client, "/api/v1/products?limit=3&sort=code", "code");
        var byCodeDescending = await WalkAsync(client, "/api/v1/products?limit=3&sort=-code", "code");
        var (_, byName) = await SendAsync(client, HttpMethod.Get, "/api/v1/products?search=tornillo&sort=code");
        var (_, byCodeSearch) = await SendAsync(client, HttpMethod.Get, "/api/v1/products?search=c-0");

        Assert.Multiple(() =>
        {
            Assert.That(byCode.Values, Is.EqualTo(new[] { "A-01", "A-10", "B-02", "C-03" }));
            Assert.That(byCode.Pages, Is.EqualTo(2));
            Assert.That(byCodeDescending.Values, Is.EqualTo(new[] { "C-03", "B-02", "A-10", "A-01" }));
            Assert.That(Codes(byName), Is.EqualTo(new[] { "A-01", "A-10" }));
            Assert.That(Codes(byCodeSearch), Is.EqualTo(new[] { "C-03" }));
        });
    }

    [Test]
    public async Task Reading_scope_alone_is_not_enough()
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company, ApiScopes.DocumentsRead);

        var (status, _) = await SendAsync(client, HttpMethod.Get, "/api/v1/products");

        Assert.That(status, Is.EqualTo(403));
    }

    private static ProductBody Product(string code) => new() { Code = code, Name = "Producto " + code, UnitMeasureId = 70, UnitPrice = 1000m };

    private static async Task<Guid> CreateAsync(HttpClient client, object body)
    {
        var (status, created) = await SendAsync(client, HttpMethod.Post, "/api/v1/products", body);
        Assert.That(status, Is.EqualTo(201), created.ToString());
        return created.GetProperty("id").GetGuid();
    }

    private static string? Field(JsonElement body, string field) =>
        body.TryGetProperty("errors", out var errors) && errors.TryGetProperty(field, out var messages) ? messages[0].GetString() : null;

    private static IReadOnlyList<string> Codes(JsonElement body) =>
        body.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("code").GetString()!).ToList();

    private static async Task<Item> StoredAsync(Guid company, Guid id)
    {
        await using var scope = MastersHost.Api.OpenScope(null, null, ActorType.System);
        return await scope.Db.Set<Item>().IgnoreQueryFilters().AsNoTracking().SingleAsync(item => item.CompanyId == company && item.Id == id);
    }
}

/// <summary>Cuerpo de alta o edición de un producto tal como lo manda el panel.</summary>
public record ProductBody
{
    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("unit_measure_id")]
    public int? UnitMeasureId { get; init; }

    [JsonPropertyName("type_item_identification_id")]
    public int? TypeItemIdentificationId { get; init; }

    [JsonPropertyName("unit_price")]
    public decimal? UnitPrice { get; init; }

    [JsonPropertyName("tax_id")]
    public int? TaxId { get; init; }

    [JsonPropertyName("tax_percent")]
    public decimal? TaxPercent { get; init; }

    [JsonPropertyName("tax_per_unit_amount")]
    public decimal? TaxPerUnitAmount { get; init; }
}

/// <summary>Cuerpo de edición, con el estado.</summary>
public sealed record ProductUpdate : ProductBody
{
    [JsonPropertyName("is_active")]
    public bool? IsActive { get; init; }
}
