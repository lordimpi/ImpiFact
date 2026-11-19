using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using ImpiFact.Integration.Tests.DocumentIssue;
using ImpiFact.Integration.Tests.Protection;
using ImpiFact.Migrator;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace ImpiFact.Integration.Tests.Migrator;

/// <summary>
/// <c>seed-demo</c> deja lista una empresa de prueba en habilitación (ambiente de pruebas de la DIAN) para probar la
/// API en local desde Scalar: empresa, software, resoluciones, certificado autofirmado y una API key nueva en cada
/// corrida. Se prueba por el punto de entrada del Migrator y, con la llave que imprime, contra la API real sobre la
/// misma base.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class SeedDemoTests
{
    private const string CertificatePassword = "seed-demo-test-password-7f3a";
    private const string DemoNit = "900123456";

    /// <summary>PIN ficticio que <c>seed-demo</c> le pone al software demo.</summary>
    private const string DemoSoftwarePin = "58217";

    private ProtectionScratch _protection = null!;

    [SetUp]
    public void CreateScratch() => _protection = new ProtectionScratch();

    [TearDown]
    public void RemoveScratch() => _protection.Dispose();

    [Test]
    public async Task Seed_demo_creates_a_company_ready_to_issue_in_habilitation()
    {
        var connectionString = await MigratedDatabaseAsync();

        var (exitCode, output, error) = await SeedAsync(connectionString, "Development");

        Assert.That(exitCode, Is.Zero, error);
        var key = PrintedKey(output);
        await using var connection = new NpgsqlConnection(connectionString);
        var company = await connection.QuerySingleAsync<(Guid Id, string Dv, string Name)>(
            "select id, dv, name from companies where identification_number = @DemoNit", new { DemoNit });
        var environments = (await connection.QueryAsync<int>(
            "select type_environment_id from company_environments where company_id = @Id", new { company.Id })).ToList();
        var software = await connection.QuerySingleAsync<(string Identifier, string PinCipher)>(
            "select software_identifier, pin_cipher from company_software where company_id = @Id", new { company.Id });
        var resolutions = (await connection.QueryAsync<(int Type, string? Prefix, string? Number, long From, long To, bool Active)>(
            "select document_type_id, prefix, resolution_number, range_from, range_to, is_active from resolutions where company_id = @Id order by document_type_id",
            new { company.Id })).ToList();
        var certificate = await connection.QuerySingleAsync<(string Subject, bool Active)>(
            "select subject, is_active from certificates where company_id = @Id", new { company.Id });
        var apiKey = await connection.QuerySingleAsync<(string Scopes, string KeyHash)>(
            "select scopes, key_hash from api_keys where company_id = @Id", new { company.Id });

        Assert.Multiple(() =>
        {
            Assert.That(company.Dv, Is.EqualTo("8"));
            Assert.That(company.Name, Is.EqualTo("EMPRESA DEMO IMPIFACT SAS"));
            Assert.That(environments, Is.Not.Empty.And.All.EqualTo(2), "Every document family starts in habilitation (2).");
            Assert.That(software.Identifier, Is.Not.Empty);
            Assert.That(software.PinCipher, Does.Not.Contain(DemoSoftwarePin), "The PIN is stored encrypted.");
            Assert.That(resolutions.Select(row => (row.Type, row.Prefix)), Is.EqualTo(new[] { (1, (string?)"SETP"), (4, "NC"), (5, "ND") }));
            Assert.That(resolutions[0], Is.EqualTo((1, (string?)"SETP", (string?)"18760000001", 990000000L, 995000000L, true)));
            Assert.That(certificate.Active, Is.True);
            Assert.That(certificate.Subject, Does.Contain("PRUEBA"));
            Assert.That(apiKey.Scopes, Is.EqualTo("documents:write,documents:read,config:write"));
            Assert.That(apiKey.KeyHash, Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))));
            Assert.That(output, Does.Contain("Copie la llave ahora: no se vuelve a mostrar"));
            Assert.That(output, Does.Contain("900123456-8"));
            Assert.That(output, Does.Contain("/scalar"));
            Assert.That(output, Does.Contain("la DIAN rechaza"));
        });
    }

    [Test]
    public async Task The_printed_key_issues_the_golden_invoice_through_the_api()
    {
        var connectionString = await MigratedDatabaseAsync();
        var (_, output, _) = await SeedAsync(connectionString, "Development");
        await using var api = new SeedDemoApiFactory(connectionString, _protection);
        using var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrintedKey(output));
        var invoice = new ByteArrayContent(File.ReadAllBytes(Path.Combine(GoldenIssueTests.GoldenRoot(), "invoice", "single-line-iva19", "input.json")));
        invoice.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await client.PostAsync("/api/v1/documents/invoices", invoice);

        var body = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
        using var json = JsonDocument.Parse(body);
        Assert.That(json.RootElement.GetProperty("cufe").GetString(), Has.Length.EqualTo(96));
    }

    [Test]
    public async Task Running_seed_demo_twice_keeps_one_company_and_prints_two_different_keys()
    {
        var connectionString = await MigratedDatabaseAsync();
        var (_, first, _) = await SeedAsync(connectionString, "Development");

        var (exitCode, second, error) = await SeedAsync(connectionString, "Development");

        Assert.That(exitCode, Is.Zero, error);
        await using var connection = new NpgsqlConnection(connectionString);
        var counts = await connection.QuerySingleAsync<(long Companies, long Software, long Resolutions, long Certificates, long Keys)>(
            """
            select (select count(*) from companies where identification_number = @DemoNit),
                   (select count(*) from company_software),
                   (select count(*) from resolutions),
                   (select count(*) from certificates),
                   (select count(*) from api_keys where company_id is not null)
            """,
            new { DemoNit });
        Assert.Multiple(() =>
        {
            Assert.That(counts, Is.EqualTo((1L, 1L, 3L, 1L, 2L)));
            Assert.That(PrintedKey(second), Is.Not.EqualTo(PrintedKey(first)));
            Assert.That(second, Does.Contain("Copie la llave ahora: no se vuelve a mostrar"));
        });
    }

    [TestCase(null)]
    [TestCase("Production")]
    [TestCase("Staging")]
    public async Task Seed_demo_refuses_to_run_outside_development_without_force(string? environment)
    {
        var connectionString = await MigratedDatabaseAsync();

        var (exitCode, output, error) = await SeedAsync(connectionString, environment);

        Assert.That(exitCode, Is.EqualTo(1));
        Assert.That(error, Does.Contain("--force"));
        Assert.That(output, Does.Not.Contain("apk_"));
        await using var connection = new NpgsqlConnection(connectionString);
        Assert.That(await connection.ExecuteScalarAsync<long>("select count(*) from companies"), Is.Zero);
    }

    [Test]
    public async Task Seed_demo_with_force_runs_outside_development()
    {
        var connectionString = await MigratedDatabaseAsync();

        var (exitCode, output, error) = await SeedAsync(connectionString, "Production", "--force");

        Assert.That(exitCode, Is.Zero, error);
        Assert.That(PrintedKey(output), Does.StartWith("apk_"));
    }

    [Test]
    public async Task Seed_demo_output_never_contains_the_certificate_password_or_the_pin()
    {
        var connectionString = await MigratedDatabaseAsync();

        var (exitCode, output, error) = await SeedAsync(connectionString, "Development");

        Assert.That(exitCode, Is.Zero, error);
        Assert.That(output + error, Does.Not.Contain(CertificatePassword));
        Assert.That(output + error, Does.Not.Contain(DemoSoftwarePin));
    }

    [Test]
    public async Task Seed_demo_on_a_database_without_migrations_asks_to_run_migrate_first()
    {
        var (exitCode, _, error) = await SeedAsync(MigratorDatabase.NewDatabase(), "Development");

        Assert.That(exitCode, Is.EqualTo(1));
        Assert.That(error, Does.Contain("migrate"));
    }

    [Test]
    public async Task The_usage_lists_seed_demo()
    {
        var (exitCode, _, error) = await RunAsync(new Dictionary<string, string?>(), "seed-demo", "--everything");

        Assert.That(exitCode, Is.EqualTo(2));
        Assert.That(error, Does.Contain("seed-demo [--force]"));
    }

    private static async Task<string> MigratedDatabaseAsync()
    {
        var connectionString = MigratorDatabase.NewDatabase();
        var (exitCode, _, error) = await RunAsync(Settings(connectionString), "migrate");
        Assert.That(exitCode, Is.Zero, error);
        return connectionString;
    }

    private Task<(int ExitCode, string Output, string Error)> SeedAsync(string connectionString, string? environment, params string[] extra)
    {
        var settings = Settings(connectionString);
        foreach (var (name, value) in _protection.LocalAesGcmSettings())
        {
            settings[name] = value;
        }

        settings["DOTNET_ENVIRONMENT"] = environment;
        return RunAsync(settings, ["seed-demo", .. extra]);
    }

    private static Dictionary<string, string?> Settings(string connectionString) => new()
    {
        ["Database:Provider"] = TestDatabase.ProviderName,
        ["Database:ConnectionString"] = connectionString,
    };

    private static string PrintedKey(string output) =>
        output.Split('\n', StringSplitOptions.TrimEntries).Single(line => line.StartsWith("apk_", StringComparison.Ordinal));

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(Dictionary<string, string?> settings, params string[] args)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await MigratorApp.RunAsync(args, configuration, output, error, () => CertificatePassword, CancellationToken.None);

        return (exitCode, output.ToString(), error.ToString());
    }

    /// <summary>La API real sobre la base que preparó el Migrator, con la misma llave maestra y carpeta de certificados.</summary>
    private sealed class SeedDemoApiFactory(string connectionString, ProtectionScratch protection) : WebApplicationFactory<Program>
    {
        private readonly ECDsa _signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:ConnectionString", connectionString);
            builder.UseSetting("Secrets:LocalAesGcm:MasterKeyFile", protection.MasterKeyFile);
            builder.UseSetting("Secrets:LocalAesGcm:CertificateDirectory", protection.CertificateDirectory);
            builder.UseSetting("Storage:Local:Root", Path.Combine(protection.Root, "storage"));
            builder.UseSetting("Jwt:SigningKey", _signingKey.ExportPkcs8PrivateKeyPem());
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _signingKey.Dispose();
            }
        }
    }
}
