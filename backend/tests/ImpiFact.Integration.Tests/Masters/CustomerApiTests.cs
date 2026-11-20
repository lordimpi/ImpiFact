using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Masters;
using ImpiFact.Integration.Tests.Companies;
using ImpiFact.Integration.Tests.Model;
using Microsoft.EntityFrameworkCore;
using static ImpiFact.Integration.Tests.Masters.MastersHost;

namespace ImpiFact.Integration.Tests.Masters;

/// <summary>
/// Clientes de la empresa por la API y con PostgreSQL: alta, edición y desactivación con las reglas del cliente de la
/// emisión, unicidad por tipo y número de identificación, aislamiento por empresa y listado paginado, ordenado y filtrado
/// en el servidor.
/// </summary>
[Category("Integration")]
public sealed class CustomerApiTests
{
    [Test]
    public async Task Create_answers_201_with_its_location_and_get_returns_every_field()
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company);
        var (nit, dv) = CompaniesHost.NewNit();

        using var response = await client.PostAsync("/api/v1/customers", Json(Customer(nit, dv) with
        {
            Phone = "6017654321", Address = "CARRERA 7 # 71-21", Email = "facturacion@cliente.com", MunicipalityId = 1006,
            TypeOrganizationId = 1, TypeRegimeId = 2, TypeLiabilityId = 14, MerchantRegistration = "0000000-00",
        }));
        var created = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var id = created.GetProperty("id").GetGuid();
        var (status, fetched) = await SendAsync(client, HttpMethod.Get, $"/api/v1/customers/{id}");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), created.ToString());
            Assert.That(response.Headers.Location?.ToString(), Is.EqualTo($"/api/v1/customers/{id}"));
            Assert.That(status, Is.EqualTo(200));
            Assert.That(fetched.ToString(), Is.EqualTo(created.ToString()));
            Assert.That(fetched.GetProperty("type_document_identification_id").GetInt32(), Is.EqualTo(6));
            Assert.That(fetched.GetProperty("identification_number").GetString(), Is.EqualTo(nit));
            Assert.That(fetched.GetProperty("dv").GetString(), Is.EqualTo(dv));
            Assert.That(fetched.GetProperty("name").GetString(), Is.EqualTo("CLIENTE " + nit));
            Assert.That(fetched.GetProperty("phone").GetString(), Is.EqualTo("6017654321"));
            Assert.That(fetched.GetProperty("address").GetString(), Is.EqualTo("CARRERA 7 # 71-21"));
            Assert.That(fetched.GetProperty("email").GetString(), Is.EqualTo("facturacion@cliente.com"));
            Assert.That(fetched.GetProperty("municipality_id").GetInt32(), Is.EqualTo(1006));
            Assert.That(fetched.GetProperty("type_organization_id").GetInt32(), Is.EqualTo(1));
            Assert.That(fetched.GetProperty("type_regime_id").GetInt32(), Is.EqualTo(2));
            Assert.That(fetched.GetProperty("type_liability_id").GetInt32(), Is.EqualTo(14));
            Assert.That(fetched.GetProperty("merchant_registration").GetString(), Is.EqualTo("0000000-00"));
            Assert.That(fetched.GetProperty("is_active").GetBoolean(), Is.True);
        });
        var stored = await StoredAsync(company, id);
        Assert.That((stored.CompanyId, stored.IdentificationNumber, stored.Dv, stored.IsActive), Is.EqualTo((company, nit, dv, true)));
    }

    [Test]
    public async Task The_issue_rules_reject_a_nit_without_its_check_digit_a_wrong_one_and_bad_fields()
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company);
        var (nit, dv) = CompaniesHost.NewNit();
        var wrong = ((int.Parse(dv, CultureInfo.InvariantCulture) + 1) % 10).ToString(CultureInfo.InvariantCulture);

        var (withoutDv, missing) = await SendAsync(client, HttpMethod.Post, "/api/v1/customers", Customer(nit, null));
        var (wrongDv, invalid) = await SendAsync(client, HttpMethod.Post, "/api/v1/customers", Customer(nit, wrong));
        var (badFields, errors) = await SendAsync(client, HttpMethod.Post, "/api/v1/customers", new CustomerBody
        {
            TypeDocumentIdentificationId = 3, IdentificationNumber = "10-20", Name = " ", Email = "no-es-correo",
        });
        var (badCatalog, catalog) = await SendAsync(client, HttpMethod.Post, "/api/v1/customers", Customer(nit, dv) with { MunicipalityId = 999_999 });

        Assert.Multiple(() =>
        {
            Assert.That(withoutDv, Is.EqualTo(422));
            Assert.That(Field(missing, "dv"), Is.EqualTo("El campo dv es obligatorio cuando type document identification id es 6."));
            Assert.That(wrongDv, Is.EqualTo(422));
            Assert.That(Field(invalid, "dv"), Is.EqualTo("Error, el digito de verificacion no es valido para este NIT."));
            Assert.That(badFields, Is.EqualTo(422));
            Assert.That(Field(errors, "identification_number"), Is.EqualTo("identification number sólo debe contener letras y números."));
            Assert.That(Field(errors, "name"), Is.EqualTo("El campo name es obligatorio."));
            Assert.That(Field(errors, "email"), Is.EqualTo("email no es un correo válido"));
            Assert.That(badCatalog, Is.EqualTo(422));
            Assert.That(Field(catalog, "municipality_id"), Is.EqualTo("municipality id es inválido."));
        });
        Assert.That(await CountAsync(company), Is.Zero, "Nothing is stored when the data is invalid.");
    }

    [Test]
    public async Task The_same_identification_twice_in_a_company_is_a_conflict_but_another_type_or_company_is_not()
    {
        var company = await CompanyAsync();
        var other = await CompanyAsync();
        using var client = await ClientAsync(company);
        using var otherClient = await ClientAsync(other);
        var (nit, dv) = CompaniesHost.NewNit();
        var first = await CreateAsync(client, Customer(nit, dv));
        await SendAsync(client, HttpMethod.Post, $"/api/v1/customers/{first}/deactivate");

        var (again, body) = await SendAsync(client, HttpMethod.Post, "/api/v1/customers", Customer(nit, dv) with { Name = "OTRO NOMBRE" });
        var (otherType, _) = await SendAsync(client, HttpMethod.Post, "/api/v1/customers", Customer(nit, null) with { TypeDocumentIdentificationId = 3 });
        var (otherCompany, _) = await SendAsync(otherClient, HttpMethod.Post, "/api/v1/customers", Customer(nit, dv));

        Assert.Multiple(() =>
        {
            Assert.That(again, Is.EqualTo(409), "Also when the first one is deactivated.");
            Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("customer.duplicate"));
            Assert.That(body.GetProperty("message").GetString(), Is.EqualTo("Ya existe un cliente con ese tipo y número de identificación."));
            Assert.That(otherType, Is.EqualTo(201));
            Assert.That(otherCompany, Is.EqualTo(201));
        });
    }

    [Test]
    public async Task Update_replaces_every_field_reactivates_and_refuses_another_customers_identification()
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company);
        var (nit, dv) = CompaniesHost.NewNit();
        var (otherNit, otherDv) = CompaniesHost.NewNit();
        var id = await CreateAsync(client, Customer(nit, dv) with { Phone = "3000000000", Email = "viejo@cliente.com" });
        await CreateAsync(client, Customer(otherNit, otherDv));
        await SendAsync(client, HttpMethod.Post, $"/api/v1/customers/{id}/deactivate");

        var (status, updated) = await SendAsync(client, HttpMethod.Put, $"/api/v1/customers/{id}", new CustomerUpdate
        {
            TypeDocumentIdentificationId = 6, IdentificationNumber = nit, Dv = dv, Name = "NUEVO NOMBRE", Email = "nuevo@cliente.com", IsActive = true,
        });
        var (taken, conflict) = await SendAsync(client, HttpMethod.Put, $"/api/v1/customers/{id}", new CustomerUpdate
        {
            TypeDocumentIdentificationId = 6, IdentificationNumber = otherNit, Dv = otherDv, Name = "X",
        });

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(200), updated.ToString());
            Assert.That(updated.GetProperty("name").GetString(), Is.EqualTo("NUEVO NOMBRE"));
            Assert.That(updated.GetProperty("email").GetString(), Is.EqualTo("nuevo@cliente.com"));
            Assert.That(updated.GetProperty("phone").ValueKind, Is.EqualTo(System.Text.Json.JsonValueKind.Null), "What does not arrive is emptied.");
            Assert.That(updated.GetProperty("is_active").GetBoolean(), Is.True);
            Assert.That(taken, Is.EqualTo(409));
            Assert.That(conflict.GetProperty("code").GetString(), Is.EqualTo("customer.duplicate"));
        });
        var stored = await StoredAsync(company, id);
        Assert.That((stored.Name, stored.Phone, stored.IdentificationNumber, stored.IsActive), Is.EqualTo(("NUEVO NOMBRE", (string?)null, nit, true)));
    }

    [Test]
    public async Task Deactivate_keeps_a_customer_used_by_a_document_and_hides_it_from_the_default_list()
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company);
        var (nit, dv) = CompaniesHost.NewNit();
        var id = await CreateAsync(client, Customer(nit, dv));
        await using (var scope = MastersHost.Api.OpenScope(null, null, ActorType.System))
        {
            scope.Db.Add(ModelGraph.NewDocument(company, RandomNumberGenerator.GetInt32(1, int.MaxValue), cufe: null, customerId: id));
            await scope.Db.SaveChangesAsync();
        }

        var (first, _) = await SendAsync(client, HttpMethod.Post, $"/api/v1/customers/{id}/deactivate");
        var (second, _) = await SendAsync(client, HttpMethod.Post, $"/api/v1/customers/{id}/deactivate");
        var (_, active) = await SendAsync(client, HttpMethod.Get, "/api/v1/customers");
        var (_, inactive) = await SendAsync(client, HttpMethod.Get, "/api/v1/customers?status=inactive");
        var (_, all) = await SendAsync(client, HttpMethod.Get, "/api/v1/customers?status=all");
        var (_, fetched) = await SendAsync(client, HttpMethod.Get, $"/api/v1/customers/{id}");

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(204));
            Assert.That(second, Is.EqualTo(204), "Deactivating twice answers the same.");
            Assert.That(Numbers(active), Does.Not.Contain(nit));
            Assert.That(Numbers(inactive), Is.EqualTo(new[] { nit }));
            Assert.That(Numbers(all), Does.Contain(nit));
            Assert.That(fetched.GetProperty("is_active").GetBoolean(), Is.False);
        });
        var stored = await StoredAsync(company, id);
        Assert.That((stored.IsActive, stored.DeletedAt), Is.EqualTo((false, (DateTimeOffset?)null)), "The row is never deleted.");
    }

    [Test]
    public async Task Another_companys_customer_is_not_found_and_never_listed()
    {
        var company = await CompanyAsync();
        var other = await CompanyAsync();
        using var client = await ClientAsync(company);
        using var otherClient = await ClientAsync(other);
        var (nit, dv) = CompaniesHost.NewNit();
        var foreign = await CreateAsync(otherClient, Customer(nit, dv));

        var (get, body) = await SendAsync(client, HttpMethod.Get, $"/api/v1/customers/{foreign}");
        var (put, _) = await SendAsync(client, HttpMethod.Put, $"/api/v1/customers/{foreign}", Customer(nit, dv) with { Name = "ROBADO" });
        var (deactivate, _) = await SendAsync(client, HttpMethod.Post, $"/api/v1/customers/{foreign}/deactivate");
        var (_, list) = await SendAsync(client, HttpMethod.Get, "/api/v1/customers?status=all&search=" + nit);

        Assert.Multiple(() =>
        {
            Assert.That(get, Is.EqualTo(404));
            Assert.That(body.GetProperty("code").GetString(), Is.EqualTo("customer.not_found"));
            Assert.That(put, Is.EqualTo(404));
            Assert.That(deactivate, Is.EqualTo(404));
            Assert.That(Numbers(list), Is.Empty);
        });
        var stored = await StoredAsync(other, foreign);
        Assert.That((stored.Name, stored.IsActive), Is.EqualTo(("CLIENTE " + nit, true)), "The other company's customer is untouched.");
    }

    [Test]
    public async Task The_list_walks_every_page_in_the_requested_order_with_the_cursor()
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company);
        string[] names = ["Delta", "alfa", "Charlie", "bravo", "Echo"];
        var numbers = new List<string>();
        foreach (var name in names)
        {
            var number = (1_000_000 + numbers.Count).ToString(CultureInfo.InvariantCulture) + RandomNumberGenerator.GetInt32(100, 999).ToString(CultureInfo.InvariantCulture);
            numbers.Add(number);
            await CreateAsync(client, new CustomerBody { TypeDocumentIdentificationId = 3, IdentificationNumber = number, Name = name });
        }

        var byName = await WalkAsync(client, "/api/v1/customers?limit=2&status=all", "name");
        var byNameDescending = await WalkAsync(client, "/api/v1/customers?limit=2&status=all&sort=-name", "name");
        var byNumber = await WalkAsync(client, "/api/v1/customers?limit=2&status=all&sort=identification_number", "identification_number");

        // El orden de los textos es el de la intercalación de la base (mayúsculas y tildes incluidas), así que se compara
        // con el ORDER BY de la misma base. El consumidor final no existe aquí: la empresa se creó directo en la base.
        List<string> expected;
        await using (var scope = MastersHost.Api.OpenScope(null, null, ActorType.System))
        {
            expected = await scope.Db.Set<Customer>().IgnoreQueryFilters().Where(customer => customer.CompanyId == company)
                .OrderBy(customer => customer.Name).ThenBy(customer => customer.Id).Select(customer => customer.Name).ToListAsync();
        }

        Assert.Multiple(() =>
        {
            Assert.That(byName.Values, Is.EqualTo(expected));
            Assert.That(byName.Values, Is.EquivalentTo(names));
            Assert.That(byName.Pages, Is.EqualTo(3));
            Assert.That(byNameDescending.Values, Is.EqualTo(Enumerable.Reverse(expected)), "The descending order is the exact reverse.");
            Assert.That(byNumber.Values, Is.EqualTo(numbers.Order(StringComparer.Ordinal)));
        });
    }

    // El cursor es (nombre, id): con nombres repetidos, el id desempata y ninguna fila se salta ni se repite entre páginas.
    [Test]
    public async Task Customers_with_the_same_name_are_paged_by_id_in_both_directions()
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company);
        for (var i = 0; i < 5; i++)
        {
            var number = (2_000_000 + i).ToString(CultureInfo.InvariantCulture) + RandomNumberGenerator.GetInt32(100, 999).ToString(CultureInfo.InvariantCulture);
            await CreateAsync(client, new CustomerBody { TypeDocumentIdentificationId = 3, IdentificationNumber = number, Name = "Repetido" });
        }

        var ascending = await WalkAsync(client, "/api/v1/customers?limit=2&status=all", "id");
        var descending = await WalkAsync(client, "/api/v1/customers?limit=2&status=all&sort=-name", "id");

        List<string> expected;
        await using (var scope = MastersHost.Api.OpenScope(null, null, ActorType.System))
        {
            expected = (await scope.Db.Set<Customer>().IgnoreQueryFilters().Where(customer => customer.CompanyId == company)
                .OrderBy(customer => customer.Name).ThenBy(customer => customer.Id).Select(customer => customer.Id).ToListAsync())
                .Select(id => id.ToString()).ToList();
        }

        Assert.Multiple(() =>
        {
            Assert.That(ascending.Values, Is.EqualTo(expected));
            Assert.That(ascending.Pages, Is.EqualTo(3));
            Assert.That(descending.Values, Is.EqualTo(Enumerable.Reverse(expected)), "The descending order is the exact reverse.");
        });
    }

    [Test]
    public async Task Search_finds_the_text_inside_the_number_or_the_name_ignoring_case_and_wildcards()
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company);
        await CreateAsync(client, new CustomerBody { TypeDocumentIdentificationId = 3, IdentificationNumber = "52123987", Name = "Ferretería El Tornillo" });
        await CreateAsync(client, new CustomerBody { TypeDocumentIdentificationId = 3, IdentificationNumber = "79555111", Name = "Panadería 100% Natural" });
        await CreateAsync(client, new CustomerBody { TypeDocumentIdentificationId = 3, IdentificationNumber = "80999000", Name = "Otro_Cliente" });

        var (_, byName) = await SendAsync(client, HttpMethod.Get, "/api/v1/customers?search=TORNI");
        var (_, byNumber) = await SendAsync(client, HttpMethod.Get, "/api/v1/customers?search=5551");
        var (_, percent) = await SendAsync(client, HttpMethod.Get, "/api/v1/customers?search=" + Uri.EscapeDataString("0%"));
        var (_, underscore) = await SendAsync(client, HttpMethod.Get, "/api/v1/customers?search=o_c");

        Assert.Multiple(() =>
        {
            Assert.That(Numbers(byName), Is.EqualTo(new[] { "52123987" }));
            Assert.That(Numbers(byNumber), Is.EqualTo(new[] { "79555111" }));
            Assert.That(Numbers(percent), Is.EqualTo(new[] { "79555111" }), "% is a literal, not a wildcard.");
            Assert.That(Numbers(underscore), Is.EqualTo(new[] { "80999000" }), "_ is a literal, not a wildcard.");
        });
    }

    [TestCase("limit=0", "limit")]
    [TestCase("limit=101", "limit")]
    [TestCase("status=deleted", "status")]
    [TestCase("sort=email", "sort")]
    [TestCase("cursor=nope", "cursor")]
    public async Task An_invalid_list_parameter_is_a_422(string query, string field)
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company);

        var (status, body) = await SendAsync(client, HttpMethod.Get, "/api/v1/customers?" + query);

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(422));
            Assert.That(body.GetProperty("errors").TryGetProperty(field, out _), Is.True, body.ToString());
        });
    }

    [Test]
    public async Task A_cursor_from_another_sort_is_a_422()
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company);
        for (var index = 0; index < 2; index++)
        {
            await CreateAsync(client, new CustomerBody { TypeDocumentIdentificationId = 3, IdentificationNumber = "4000000" + index, Name = "C" + index });
        }

        var (_, page) = await SendAsync(client, HttpMethod.Get, "/api/v1/customers?limit=1");
        var cursor = Uri.EscapeDataString(page.GetProperty("next_cursor").GetString()!);
        var (status, _) = await SendAsync(client, HttpMethod.Get, $"/api/v1/customers?limit=1&sort=-name&cursor={cursor}");

        Assert.That(status, Is.EqualTo(422));
    }

    [Test]
    public async Task Reading_scope_alone_is_not_enough()
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company, ApiScopes.DocumentsRead);

        var (list, _) = await SendAsync(client, HttpMethod.Get, "/api/v1/customers");
        var (create, _) = await SendAsync(client, HttpMethod.Post, "/api/v1/customers", Customer("900123456", "8"));

        Assert.Multiple(() =>
        {
            Assert.That(list, Is.EqualTo(403), "The customer portal role (documents:read) must not see every customer.");
            Assert.That(create, Is.EqualTo(403));
        });
    }

    private static CustomerBody Customer(string nit, string? dv) => new()
    {
        TypeDocumentIdentificationId = 6, IdentificationNumber = nit, Dv = dv, Name = "CLIENTE " + nit,
    };

    private static async Task<Guid> CreateAsync(HttpClient client, object body)
    {
        var (status, created) = await SendAsync(client, HttpMethod.Post, "/api/v1/customers", body);
        Assert.That(status, Is.EqualTo(201), created.ToString());
        return created.GetProperty("id").GetGuid();
    }

    private static StringContent Json(object body) =>
        new(System.Text.Json.JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");

    private static string? Field(System.Text.Json.JsonElement body, string field) =>
        body.TryGetProperty("errors", out var errors) && errors.TryGetProperty(field, out var messages) ? messages[0].GetString() : null;

    private static IReadOnlyList<string> Numbers(System.Text.Json.JsonElement body) =>
        body.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("identification_number").GetString()!).ToList();

    private static async Task<Customer> StoredAsync(Guid company, Guid id)
    {
        await using var scope = MastersHost.Api.OpenScope(null, null, ActorType.System);
        return await scope.Db.Set<Customer>().IgnoreQueryFilters().AsNoTracking().SingleAsync(customer => customer.CompanyId == company && customer.Id == id);
    }

    private static async Task<int> CountAsync(Guid company)
    {
        await using var scope = MastersHost.Api.OpenScope(null, null, ActorType.System);
        return await scope.Db.Set<Customer>().IgnoreQueryFilters().CountAsync(customer => customer.CompanyId == company);
    }
}

/// <summary>Cuerpo de alta o edición de un cliente tal como lo manda el panel.</summary>
public record CustomerBody
{
    [System.Text.Json.Serialization.JsonPropertyName("type_document_identification_id")]
    public int? TypeDocumentIdentificationId { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("identification_number")]
    public string? IdentificationNumber { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("dv")]
    public string? Dv { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string? Name { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("phone")]
    public string? Phone { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("address")]
    public string? Address { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("email")]
    public string? Email { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("municipality_id")]
    public int? MunicipalityId { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("type_organization_id")]
    public int? TypeOrganizationId { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("type_regime_id")]
    public int? TypeRegimeId { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("type_liability_id")]
    public int? TypeLiabilityId { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("merchant_registration")]
    public string? MerchantRegistration { get; init; }
}

/// <summary>Cuerpo de edición, con el estado.</summary>
public sealed record CustomerUpdate : CustomerBody
{
    [System.Text.Json.Serialization.JsonPropertyName("is_active")]
    public bool? IsActive { get; init; }
}
