using System.Buffers.Text;
using System.Text;
using ImpiFact.Integration.Tests.Companies;
using static ImpiFact.Integration.Tests.Masters.MastersHost;

namespace ImpiFact.Integration.Tests.Masters;

[Category("Integration")]
public sealed class MasterInputRegressionTests
{
    private static string Cursor(string json) => Uri.EscapeDataString(Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json)));

    private static async Task SeedAsync(HttpClient client, string prefix)
    {
        for (var index = 0; index < 3; index++)
        {
            var (status, body) = await SendAsync(client, HttpMethod.Post, "/api/v1/customers",
                new CustomerBody { TypeDocumentIdentificationId = 3, IdentificationNumber = prefix + index, Name = "Cliente " + prefix + index });
            Assert.That(status, Is.EqualTo(201), body.ToString());
        }
    }

    [TestCase("{\"S\":\"name\",\"V\":\"a\u0000b\",\"I\":\"0193f3a0-0000-7000-8000-000000000001\"}", TestName = "Cursor_with_nul_in_value")]
    [TestCase("{\"S\":\"name\",\"V\":\"a\",\"I\":12}", TestName = "Cursor_with_numeric_id")]
    [TestCase("{\"S\":\"name\",\"V\":\"a\",\"I\":\"0193f3a0-0000-7000-8000-000000000001\",\"x\":[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]}", TestName = "Cursor_deeply_nested")]
    [TestCase("{\"S\":\"name\",\"V\":\"a\\u0000b\",\"I\":\"0193f3a0-0000-7000-8000-000000000001\"}", TestName = "Cursor_with_escaped_nul_in_value")]
    [TestCase("{\"S\":\"name\",\"V\":\"a\\u001fb\",\"I\":\"0193f3a0-0000-7000-8000-000000000001\"}", TestName = "Cursor_with_escaped_control_in_value")]
    [TestCase("[1,2]", TestName = "Cursor_array")]
    [TestCase("null", TestName = "Cursor_null")]
    public async Task Forged_customer_cursor_is_a_422(string json)
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company);
        var (status, body) = await SendAsync(client, HttpMethod.Get, "/api/v1/customers?limit=1&cursor=" + Cursor(json));
        Assert.That(status, Is.EqualTo(422), body.ToString());
    }

    [TestCase("{\"S\":\"name\",\"V\":\"a\",\"I\":\"0193f3a0-0000-7000-8000-000000000001\"}", TestName = "Cursor_plain")]
    [TestCase("{\"S\":\"name\",\"V\":\"a0000b\",\"I\":\"00000000-0000-0000-0000-000000000001\"}", TestName = "Cursor_with_digits_and_low_guid")]
    public async Task Well_formed_customer_cursor_is_accepted(string json)
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company);
        var (status, body) = await SendAsync(client, HttpMethod.Get, "/api/v1/customers?limit=1&cursor=" + Cursor(json));
        Assert.That(status, Is.EqualTo(200), body.ToString());
    }

    [Test]
    public async Task Cursor_with_invalid_utf8_is_a_422()
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company);
        var bytes = Encoding.UTF8.GetBytes("{\"S\":\"name\",\"V\":\"\",\"I\":\"0193f3a0-0000-7000-8000-000000000001\"}").ToList();
        bytes.InsertRange(17, new byte[] { 0xC3, 0x28 });
        var cursor = Uri.EscapeDataString(Base64Url.EncodeToString(bytes.ToArray()));
        var (status, body) = await SendAsync(client, HttpMethod.Get, "/api/v1/customers?limit=1&cursor=" + cursor);
        Assert.That(status, Is.EqualTo(422), body.ToString());
    }

    [TestCase("customers", "a%00b")]
    [TestCase("products", "a%00b")]
    [TestCase("customers", "a%1Fb")]
    [TestCase("products", "a%0Ab")]
    public async Task Search_with_a_control_character_is_a_422_on_search(string resource, string search)
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company);

        var (status, body) = await SendAsync(client, HttpMethod.Get, $"/api/v1/{resource}?search={search}");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(422), body.ToString());
            Assert.That(Field(body, "search"), Is.EqualTo("search no debe contener caracteres de control."), body.ToString());
        });
    }

    [Test]
    public async Task Search_with_plain_text_still_filters()
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company);
        var (created, createdBody) = await SendAsync(client, HttpMethod.Post, "/api/v1/customers",
            new CustomerBody { TypeDocumentIdentificationId = 3, IdentificationNumber = "51110010", Name = "Ferretería Ñandú" });
        Assert.That(created, Is.EqualTo(201), createdBody.ToString());

        var (status, body) = await SendAsync(client, HttpMethod.Get, "/api/v1/customers?search=" + Uri.EscapeDataString("ñandú"));

        Assert.That(status, Is.EqualTo(200), body.ToString());
        var numbers = body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("identification_number").GetString()!).ToList();
        Assert.That(numbers, Is.EqualTo(new[] { "51110010" }));
    }

    [Test]
    public async Task Forged_cursor_with_other_company_id_never_leaks_rows()
    {
        var company = await CompanyAsync();
        var other = await CompanyAsync();
        using var client = await ClientAsync(company);
        using var otherClient = await ClientAsync(other);
        var (nit, _) = CompaniesHost.NewNit();
        await SeedAsync(otherClient, "7" + nit[..6]);
        await SeedAsync(client, "8" + nit[..6]);
        var json = "{\"S\":\"name\",\"V\":\"\",\"I\":\"00000000-0000-0000-0000-000000000001\"}";
        var (status, body) = await SendAsync(client, HttpMethod.Get, "/api/v1/customers?limit=100&status=all&cursor=" + Cursor(json));
        Assert.That(status, Is.EqualTo(200), body.ToString());
        var numbers = body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("identification_number").GetString()!).ToList();
        Assert.That(numbers, Has.All.StartsWith("8"));
        Assert.That(numbers, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task Backslash_in_search_is_a_literal()
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company);
        var (s1, b1) = await SendAsync(client, HttpMethod.Post, "/api/v1/customers",
            new CustomerBody { TypeDocumentIdentificationId = 3, IdentificationNumber = "51110001", Name = @"Uno\Dos" });
        var (s2, b2) = await SendAsync(client, HttpMethod.Post, "/api/v1/customers",
            new CustomerBody { TypeDocumentIdentificationId = 3, IdentificationNumber = "51110002", Name = "UnoDos" });
        Assert.That((s1, s2), Is.EqualTo((201, 201)), b1 + " " + b2);
        var (status, body) = await SendAsync(client, HttpMethod.Get, "/api/v1/customers?search=" + Uri.EscapeDataString(@"o\D"));
        Assert.That(status, Is.EqualTo(200), body.ToString());
        var numbers = body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("identification_number").GetString()!).ToList();
        Assert.That(numbers, Is.EqualTo(new[] { "51110001" }));
    }

    [TestCase("name")]
    [TestCase("phone")]
    [TestCase("address")]
    [TestCase("email")]
    [TestCase("merchant_registration")]
    public async Task Customer_text_with_nul_is_a_422_on_its_field(string field)
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company);
        var customer = WithNul(new CustomerBody { TypeDocumentIdentificationId = 3, IdentificationNumber = "51110003", Name = "Cliente" }, field);

        var (status, body) = await SendAsync(client, HttpMethod.Post, "/api/v1/customers", customer);

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(422), body.ToString());
            Assert.That(Field(body, field), Is.EqualTo($"{field.Replace('_', ' ')} no debe contener caracteres de control."), body.ToString());
        });
    }

    [Test]
    public async Task Customer_update_with_nul_is_a_422_and_keeps_the_row()
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company);
        var (created, createdBody) = await SendAsync(client, HttpMethod.Post, "/api/v1/customers",
            new CustomerBody { TypeDocumentIdentificationId = 3, IdentificationNumber = "51110004", Name = "Cliente original" });
        Assert.That(created, Is.EqualTo(201), createdBody.ToString());
        var id = createdBody.GetProperty("id").GetGuid();

        var (status, body) = await SendAsync(client, HttpMethod.Put, $"/api/v1/customers/{id}",
            new CustomerUpdate { TypeDocumentIdentificationId = 3, IdentificationNumber = "51110004", Name = "A\u0000B" });
        var (read, stored) = await SendAsync(client, HttpMethod.Get, $"/api/v1/customers/{id}");

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(422), body.ToString());
            Assert.That(Field(body, "name"), Is.EqualTo("name no debe contener caracteres de control."), body.ToString());
            Assert.That(read, Is.EqualTo(200), stored.ToString());
            Assert.That(stored.GetProperty("name").GetString(), Is.EqualTo("Cliente original"));
        });
    }

    [Test]
    public async Task Customer_address_keeps_accepting_line_breaks_and_tabs()
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company);

        var (status, body) = await SendAsync(client, HttpMethod.Post, "/api/v1/customers",
            new CustomerBody { TypeDocumentIdentificationId = 3, IdentificationNumber = "51110005", Name = "Cliente", Address = "Calle 1\n\tPiso 2" });

        Assert.That(status, Is.EqualTo(201), body.ToString());
    }

    [TestCase("code")]
    [TestCase("name")]
    [TestCase("description")]
    public async Task Product_text_with_nul_is_a_422_on_its_field(string field)
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company);
        var product = new ProductBody { Code = "P-NUL", Name = "Producto", UnitMeasureId = 70, UnitPrice = 1000m };
        product = field switch
        {
            "code" => product with { Code = "P\u0000NUL" },
            "name" => product with { Name = "Pro\u0000ducto" },
            _ => product with { Description = "Des\u0000cripción" },
        };

        var (status, body) = await SendAsync(client, HttpMethod.Post, "/api/v1/products", product);

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(422), body.ToString());
            Assert.That(Field(body, field), Is.EqualTo($"{field} no debe contener caracteres de control."), body.ToString());
        });
    }

    [Test]
    public async Task Product_update_with_nul_is_a_422()
    {
        var company = await CompanyAsync();
        using var client = await ClientAsync(company);
        var (created, createdBody) = await SendAsync(client, HttpMethod.Post, "/api/v1/products",
            new ProductBody { Code = "P-UPD", Name = "Producto", UnitMeasureId = 70, UnitPrice = 1000m });
        Assert.That(created, Is.EqualTo(201), createdBody.ToString());
        var id = createdBody.GetProperty("id").GetGuid();

        var (status, body) = await SendAsync(client, HttpMethod.Put, $"/api/v1/products/{id}",
            new ProductUpdate { Code = "P-UPD", Name = "Pro\u0000ducto", UnitMeasureId = 70, UnitPrice = 1000m });

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(422), body.ToString());
            Assert.That(Field(body, "name"), Is.EqualTo("name no debe contener caracteres de control."), body.ToString());
        });
    }

    private static CustomerBody WithNul(CustomerBody customer, string field) => field switch
    {
        "name" => customer with { Name = "A\u0000B" },
        "phone" => customer with { Phone = "300\u00001234" },
        "address" => customer with { Address = "Calle\u00001" },
        "email" => customer with { Email = "a\u0000b@example.com" },
        _ => customer with { MerchantRegistration = "00\u000000" },
    };

    private static string? Field(System.Text.Json.JsonElement body, string field) =>
        body.TryGetProperty("errors", out var errors) && errors.TryGetProperty(field, out var messages) ? messages[0].GetString() : null;
}
