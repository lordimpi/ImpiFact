using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Features.Companies;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Documents;
using ImpiFact.Integration.Tests.Model;
using ImpiFact.Integration.Tests.Protection;
using Microsoft.EntityFrameworkCore;
using static ImpiFact.Integration.Tests.Companies.CompanyApiTests;

namespace ImpiFact.Integration.Tests.Companies;

[Category("Integration")]
public sealed class CompanyReadIsolationTests
{
    private const string TechnicalKey = "ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12";

    private static CompanyApiFactory Api => CompaniesHost.Api;

    private static readonly string[] ReadPaths =
    [
        "/api/v1/companies/{0}",
        "/api/v1/companies/{0}/environments",
        "/api/v1/companies/{0}/software",
        "/api/v1/companies/{0}/resolutions",
        "/api/v1/companies/{0}/setup-status",
    ];

    private static string P(string path, string nit) => string.Format(CultureInfo.InvariantCulture, path, nit);

    [Test]
    public async Task nit_variants_platform_and_foreign_keys_get_403()
    {
        var (company, nit) = await CreateCompanyAsync();
        var (other, otherNit) = await CreateCompanyAsync();
        var dv = Nit.CheckDigit(nit).ToString(CultureInfo.InvariantCulture);
        using var platform = await Api.ClientWithNewKeyAsync(null, ApiScopes.Platform);
        using var platformAll = await Api.ClientWithNewKeyAsync(null, ApiScopes.Platform, ApiScopes.ConfigWrite);
        string[] variants = ["0" + nit, "%20" + nit, nit + "%20", nit + dv, nit + "-" + dv, nit + "%2F", Uri.EscapeDataString(nit + "\t")];

        Assert.Multiple(async () =>
        {
            foreach (var path in ReadPaths)
            {
                foreach (var variant in variants)
                {
                    using var response = await company.GetAsync(P(path, variant));
                    var body = await response.Content.ReadAsStringAsync();
                    Assert.That((int)response.StatusCode, Is.AnyOf(403, 404), $"{path} {variant}: {body}");
                    Assert.That(body, Does.Not.Contain("business_name"), $"{path} {variant}");
                }

                using var byPlatform = await platform.GetAsync(P(path, nit));
                Assert.That(byPlatform.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "platform " + path);
                using var byPlatformAll = await platformAll.GetAsync(P(path, nit));
                // Una llave de plataforma con alcances de empresa se rechaza ya en la autenticación (401).
                Assert.That((int)byPlatformAll.StatusCode, Is.AnyOf(401, 403), "platform+config " + path);
                using var byOther = await other.GetAsync(P(path, nit));
                Assert.That(byOther.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "other company " + path);
                using var own = await other.GetAsync(P(path, otherNit));
                Assert.That(own.StatusCode, Is.EqualTo(HttpStatusCode.OK), "own " + path);
            }
        });
    }

    [Test]
    public async Task no_secret_in_any_read_body_or_log_and_no_dian_call()
    {
        var (company, nit) = await CreateCompanyAsync();
        const string pin = "97531";
        using (var c = await company.PutAsJsonAsync($"/api/v1/companies/{nit}/certificate",
            new { certificate = Convert.ToBase64String(TestPfx.Rsa()), password = TestPfx.Password }))
        {
            Assert.That(c.StatusCode, Is.EqualTo(HttpStatusCode.OK), await c.Content.ReadAsStringAsync());
        }

        (await company.PutAsJsonAsync($"/api/v1/companies/{nit}/software", new { id = "soft-verif", pin, test_set_id = "set-verif" })).Dispose();
        (await company.PutAsJsonAsync($"/api/v1/companies/{nit}/resolution", new Dictionary<string, object>
        {
            ["type_document_id"] = 1, ["prefix"] = "SETP", ["resolution"] = "18760000001", ["resolution_date"] = "2019-01-19",
            ["technical_key"] = TechnicalKey, ["from"] = 990000000, ["to"] = 995000000,
            ["date_from"] = "2019-01-19", ["date_to"] = "2030-01-19",
        })).Dispose();

        string cipher;
        await using (var scope = Api.OpenScope(null, null, ActorType.System))
        {
            var id = await CompanyIdAsync(scope.Db, nit);
            cipher = (await Rows<CompanySoftware>(scope.Db, id).SingleAsync()).PinCipher;
        }

        var dianBefore = Api.Dian.Requests.Count;
        var logsBefore = Api.Logs.Entries.Count;
        var bodies = new List<string>();
        foreach (var path in ReadPaths)
        {
            using var response = await company.GetAsync(P(path, nit));
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), path);
            bodies.Add(await response.Content.ReadAsStringAsync());
        }

        var logs = string.Join("\n", Api.Logs.Entries.Skip(logsBefore));
        Assert.Multiple(() =>
        {
            Assert.That(Api.Dian.Requests.Count, Is.EqualTo(dianBefore), "No read calls the DIAN.");
            foreach (var text in bodies.Append(logs))
            {
                Assert.That(text, Does.Not.Contain(pin));
                Assert.That(text, Does.Not.Contain(cipher));
                Assert.That(text, Does.Not.Contain(TechnicalKey));
                Assert.That(text, Does.Not.Contain(TestPfx.Password));
                Assert.That(text, Does.Not.Contain("\"pin\"").And.Not.Contain("\"technical_key\"").And.Not.Contain("password"));
            }
        });
        TestContext.Out.WriteLine(string.Join("\n----\n", bodies));
    }

    [Test]
    public async Task test_set_answers_of_another_company_do_not_leak()
    {
        var (_, nitA) = await CreateCompanyAsync();
        var (companyB, nitB) = await CreateCompanyAsync();
        await using (var scope = Api.OpenScope(null, null, ActorType.System))
        {
            var companyId = await CompanyIdAsync(scope.Db, nitA);
            var document = ModelGraph.NewDocument(companyId, 990000900, cufe: null);
            scope.Db.Add(document);
            scope.Db.Add(new DianSubmission
            {
                CompanyId = companyId, DocumentId = document.Id, Attempt = 1, Operation = DianOperation.GetStatusZip,
                TypeEnvironmentId = 2, IsValid = true, StartedAt = CompaniesHost.Now, FinishedAt = CompaniesHost.Now,
            });
            await scope.Db.SaveChangesAsync();
        }

        using var status = await Json(await companyB.GetAsync($"/api/v1/companies/{nitB}/setup-status"));
        Assert.That(status.RootElement.GetProperty("test_set_accepted").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public async Task api_keys_company_key_sees_only_own_and_tampered_cursor_cannot_cross()
    {
        var (_, nitA) = await CreateCompanyAsync();
        var (_, nitB) = await CreateCompanyAsync();
        Guid a, b;
        await using (var scope = Api.OpenScope(null, null, ActorType.System))
        {
            a = await CompanyIdAsync(scope.Db, nitA);
            b = await CompanyIdAsync(scope.Db, nitB);
        }

        using var keyA = await Api.ClientWithNewKeyAsync(a, ApiScopes.ConfigWrite);
        using var keyB = await Api.ClientWithNewKeyAsync(b, ApiScopes.ConfigWrite);
        using var readerA = await Api.ClientWithNewKeyAsync(a, ApiScopes.DocumentsRead, ApiScopes.DocumentsWrite);
        using var platform = await Api.ClientWithNewKeyAsync(null, ApiScopes.Platform);
        using var companyPlatform = await Api.ClientWithNewKeyAsync(a, ApiScopes.Platform);

        var idsA = await IdsAsync(a);
        var idsB = await IdsAsync(b);
        var platformIds = await IdsAsync(null);

        string Cursor(long ticks, Guid id) =>
            Base64Url.EncodeToString(Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{ticks}.{id:N}")));

        var tampered = new[]
        {
            Cursor(DateTimeOffset.MaxValue.UtcTicks, Guid.Empty),
            Cursor(DateTimeOffset.MaxValue.UtcTicks, idsB[0]),
            Cursor(0, Guid.Empty),
            Cursor(1, idsB[0]),
        };

        Assert.Multiple(async () =>
        {
            var listA = await ListAsync(keyA, "?limit=100");
            Assert.That(listA.Status, Is.EqualTo(HttpStatusCode.OK), listA.Body);
            Assert.That(listA.Ids, Is.EquivalentTo(idsA).Or.SubsetOf(idsA), "company key sees only its company keys");
            Assert.That(listA.Ids.Intersect(idsB), Is.Empty);
            Assert.That(listA.Body, Does.Not.Contain("key_hash"));
            foreach (var cursor in tampered)
            {
                var page = await ListAsync(keyA, $"?limit=100&cursor={Uri.EscapeDataString(cursor)}");
                Assert.That(page.Status, Is.EqualTo(HttpStatusCode.OK), cursor + " " + page.Body);
                Assert.That(page.Ids.Except(idsA), Is.Empty, "tampered cursor " + cursor);
            }

            var reader = await ListAsync(readerA, "");
            Assert.That(reader.Status, Is.EqualTo(HttpStatusCode.Forbidden), "documents scopes cannot list keys");
            var cp = await ListAsync(companyPlatform, "");
            Assert.That(cp.Status, Is.EqualTo(HttpStatusCode.Forbidden), "company key with platform scope");
            var pl = await ListAsync(platform, "?limit=100");
            Assert.That(pl.Status, Is.EqualTo(HttpStatusCode.OK), pl.Body);
            Assert.That(pl.Ids.Intersect(idsA.Concat(idsB)), Is.Empty, "platform list has no company key");
            Assert.That(pl.Ids.Except(platformIds), Is.Empty);
            var pc = await ListAsync(companyPlatform, "?limit=100");
            Assert.That(pc.Body, Does.Not.Contain(idsB[0].ToString()));
        });
    }

    [Test]
    public async Task platform_companies_search_specials_and_cursor_tampering()
    {
        using var platform = await Api.ClientWithNewKeyAsync(null, ApiScopes.Platform);
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(5));
        var (nit, dv) = CompaniesHost.NewNit();
        using (var created = await platform.PostAsJsonAsync($"/api/v1/companies/{nit}/{dv}", CompanyBody(nit, businessName: $"CIEN%_\\ {token} SAS")))
        {
            Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK), await created.Content.ReadAsStringAsync());
        }

        var (nit2, dv2) = CompaniesHost.NewNit();
        using (var created = await platform.PostAsJsonAsync($"/api/v1/companies/{nit2}/{dv2}", CompanyBody(nit2, businessName: $"CIENXY {token} SAS")))
        {
            Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        async Task<(HttpStatusCode Status, string Body, List<string> Nits)> Search(string query)
        {
            using var response = await platform.GetAsync("/api/v1/platform/companies?limit=100&" + query);
            var body = await response.Content.ReadAsStringAsync();
            var nits = response.StatusCode == HttpStatusCode.OK
                ? JsonDocument.Parse(body).RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("nit").GetString()!).ToList()
                : [];
            return (response.StatusCode, body, nits);
        }

        string NameCursor(string name, Guid id) => Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new { N = name, I = id }));

        Assert.Multiple(async () =>
        {
            var percent = await Search("search=" + Uri.EscapeDataString($"N%_\\ {token}"));
            Assert.That(percent.Status, Is.EqualTo(HttpStatusCode.OK), percent.Body);
            Assert.That(percent.Nits, Is.EqualTo(new[] { nit }), "literal % _ \\ match only the literal name");
            var underscore = await Search("search=" + Uri.EscapeDataString($"CIEN__ {token}"));
            Assert.That(underscore.Nits, Is.Empty, "_ is literal");
            var pct = await Search("search=" + Uri.EscapeDataString("%"));
            Assert.That(pct.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(pct.Nits, Does.Contain(nit).And.Not.Contain(nit2));
            var backslash = await Search("search=" + Uri.EscapeDataString("\\"));
            Assert.That(backslash.Nits, Does.Contain(nit).And.Not.Contain(nit2));
            foreach (var control in new[] { "\u0000", "a\u0001b", "\u007f", "\u0085" })
            {
                var c = await Search("search=" + Uri.EscapeDataString(control));
                Assert.That(c.Status, Is.EqualTo(HttpStatusCode.UnprocessableEntity), "control " + (int)control[^1]);
            }

            var quote = await Search("search=" + Uri.EscapeDataString("' or 1=1 --"));
            Assert.That(quote.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(quote.Nits, Is.Empty);
            foreach (var cursor in new[] { NameCursor("", Guid.NewGuid()), NameCursor("'); drop table companies;--", Guid.NewGuid()),
                         NameCursor(new string('z', 700), Guid.NewGuid()), NameCursor("ñ", Guid.NewGuid()) })
            {
                var r = await Search("cursor=" + Uri.EscapeDataString(cursor));
                Assert.That(r.Status, Is.EqualTo(HttpStatusCode.OK), r.Body);
            }

            foreach (var bad in new[] { NameCursor("a\u0000", Guid.NewGuid()), Base64Url.EncodeToString(Encoding.UTF8.GetBytes("{\"N\":\"a\",\"I\":\"x\"}")),
                         Base64Url.EncodeToString(Encoding.UTF8.GetBytes("null")), Base64Url.EncodeToString(Encoding.UTF8.GetBytes("[1]")),
                         Base64Url.EncodeToString(new byte[] { 0xff, 0xfe, 0x00 }) })
            {
                var r = await Search("cursor=" + Uri.EscapeDataString(bad));
                Assert.That(r.Status, Is.EqualTo(HttpStatusCode.UnprocessableEntity), "bad cursor " + r.Body);
            }

            Guid someCompany;
            await using (var scope = Api.OpenScope(null, null, ActorType.System))
            {
                someCompany = await CompanyIdAsync(scope.Db, nit);
            }

            using var withCompany = await Api.ClientWithNewKeyAsync(someCompany, ApiScopes.Platform);
            using var r2 = await withCompany.GetAsync("/api/v1/platform/companies");
            Assert.That((int)r2.StatusCode, Is.AnyOf(401, 403));
        });
    }

    private static async Task<List<Guid>> IdsAsync(Guid? companyId)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        return await scope.Db.Set<ImpiFact.Domain.Identity.ApiKey>().Where(k => k.CompanyId == companyId)
            .OrderByDescending(k => k.CreatedAt).Select(k => k.Id).ToListAsync();
    }

    private static async Task<(HttpStatusCode Status, string Body, List<Guid> Ids)> ListAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync("/api/v1/api-keys" + query);
        var body = await response.Content.ReadAsStringAsync();
        var ids = response.StatusCode == HttpStatusCode.OK
            ? JsonDocument.Parse(body).RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList()
            : [];
        return (response.StatusCode, body, ids);
    }
}
