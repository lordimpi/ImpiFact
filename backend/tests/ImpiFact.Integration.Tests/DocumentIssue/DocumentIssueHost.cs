using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Companies;
using ImpiFact.Infrastructure.Persistence.Seeding;
using ImpiFact.Integration.Tests.Companies;
using ImpiFact.Integration.Tests.Protection;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Integration.Tests.DocumentIssue;

/// <summary>
/// Levanta un PostgreSQL desechable en Docker y la API real encima, una sola vez para las pruebas de emisión (un
/// <c>SetUpFixture</c> de NUnit solo cubre su propio espacio de nombres). El esquema sale de las migraciones reales y
/// del sincronizador de catálogos, como en un despliegue.
/// </summary>
[SetUpFixture]
[Category("Integration")]
public sealed class DocumentIssueHost
{
    private static TestDatabaseServer? container;
    private static CompanyApiFactory? factory;

    public static CompanyApiFactory Api => factory ?? throw new InvalidOperationException("The document issue API host is not running.");

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        container = TestDatabase.CreateServer();
        await container.StartAsync();
        factory = new CompanyApiFactory(container.GetConnectionString());

        await using var scope = factory.OpenScope(companyId: null, userId: null, ActorType.System);
        await scope.Db.Database.MigrateAsync();
        await new CatalogSynchronizer(scope.Db).SynchronizeAsync(CancellationToken.None);
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (factory is not null)
        {
            await factory.DisposeAsync();
        }

        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }
}

/// <summary>Una empresa emisora creada por la API real, con su API key.</summary>
public sealed record Issuer(HttpClient Client, Guid CompanyId, string Nit) : IDisposable
{
    public void Dispose() => Client.Dispose();
}

/// <summary>
/// Configura empresas emisoras por los casos de uso de Company, por la API: alta (con el NIT y los datos que pida la
/// prueba), software, certificado y resoluciones, como lo hacía <c>tools/golden-capture/capture.py</c> con el PHP.
/// </summary>
public static class IssuerSetup
{
    /// <summary>NIT de la empresa de la captura golden; solo ella lleva el correo de la captura.</summary>
    public const string GoldenNit = "900000001";

    public const string SoftwareId = "3f9d8c2e-5b1a-4e7f-9c3d-2a6b8e1f0d47";
    public const string SoftwarePin = "12345";
    public const string TechnicalKey = "fc8eac422eba16e22ffd8c6f94b3f40a6e38162c";

    /// <summary>Resoluciones de la captura golden: factura SETP con su clave técnica y notas NC y ND sin fechas.</summary>
    public static readonly object[] GoldenResolutions =
    [
        new Dictionary<string, object>
        {
            ["type_document_id"] = 1, ["prefix"] = "SETP", ["resolution"] = "18760000001", ["resolution_date"] = "2019-01-19",
            ["technical_key"] = TechnicalKey, ["from"] = 990000000, ["to"] = 995000000, ["date_from"] = "2019-01-19", ["date_to"] = "2030-01-19",
        },
        new Dictionary<string, object> { ["type_document_id"] = 4, ["prefix"] = "NC", ["from"] = 1, ["to"] = 99999999 },
        new Dictionary<string, object> { ["type_document_id"] = 5, ["prefix"] = "ND", ["from"] = 1, ["to"] = 99999999 },
    ];

    /// <summary>Datos de la empresa de la captura golden (COMPANY de capture.py).</summary>
    public static Dictionary<string, object> GoldenCompanyBody(string email = "golden@example.com") => new()
    {
        ["type_document_identification_id"] = 6,
        ["type_organization_id"] = 1,
        ["type_regime_id"] = 1,
        ["type_liability_id"] = 14,
        ["business_name"] = "EMPRESA DE PRUEBA GOLDEN SAS",
        ["merchant_registration"] = "0000000-00",
        ["municipality_id"] = 149,
        ["address"] = "CALLE 1 # 1-01",
        ["phone"] = "3000000000",
        ["email"] = email,
    };

    /// <summary>La empresa golden (NIT 900000001-2) o, con otro NIT, una igual con otro correo.</summary>
    public static async Task<Issuer> CreateAsync(
        string nit, string dv, bool withCertificate = true, bool withSoftware = true, IEnumerable<object>? resolutions = null)
    {
        var api = DocumentIssueHost.Api;
        using var platform = await api.ClientWithNewKeyAsync(null, ApiScopes.Platform);
        using var created = await platform.PostAsJsonAsync($"/api/ubl2.1/config/{nit}/{dv}", GoldenCompanyBody(nit == GoldenNit ? "golden@example.com" : $"{nit}@example.com"));
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK), await created.Content.ReadAsStringAsync());
        string token;
        using (var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync()))
        {
            token = json.RootElement.GetProperty("token").GetString()!;
        }

        var client = api.ClientWith(token);
        if (withSoftware)
        {
            await Ok(await client.PutAsJsonAsync("/api/ubl2.1/config/software", new { id = SoftwareId, pin = SoftwarePin }));
        }

        if (withCertificate)
        {
            await Ok(await client.PutAsJsonAsync("/api/ubl2.1/config/certificate",
                new { certificate = Convert.ToBase64String(TestPfx.Rsa()), password = TestPfx.Password }));
        }

        foreach (var resolution in resolutions ?? GoldenResolutions)
        {
            await Ok(await client.PutAsJsonAsync("/api/ubl2.1/config/resolution", resolution));
        }

        await using var scope = api.OpenScope(null, null, ActorType.System);
        var companyId = (await scope.Db.Set<Company>().SingleAsync(row => row.IdentificationNumber == nit)).Id;
        return new Issuer(client, companyId, nit);
    }

    private static async Task Ok(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        }
    }
}
