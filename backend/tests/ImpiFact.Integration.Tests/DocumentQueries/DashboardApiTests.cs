using System.Net;
using System.Text.Json;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Documents;
using ImpiFact.Integration.Tests.Companies;

namespace ImpiFact.Integration.Tests.DocumentQueries;

/// <summary>
/// Tablero del panel (<c>GET /api/v1/dashboard/summary</c>) contra datos sembrados en PostgreSQL. El reloj de la API
/// está fijo en <see cref="CompaniesHost.Now"/>: 15-ene-2026 a las 10:00 UTC, las 05:00 en Colombia.
/// </summary>
[Category("Integration")]
public sealed class DashboardApiTests
{
    private static readonly DateTimeOffset Now = CompaniesHost.Now;

    [Test]
    public async Task The_summary_counts_documents_by_colombian_month_kind_and_status_of_the_company_only()
    {
        var company = await DocumentSeed.CompanyAsync();
        var other = await DocumentSeed.CompanyAsync();
        var january = new DateTimeOffset(2026, 1, 10, 15, 0, 0, TimeSpan.Zero);
        await DocumentSeed.DocumentAsync(company, 1, 1, DocumentStatus.Accepted, january);
        await DocumentSeed.DocumentAsync(company, 1, 2, DocumentStatus.Accepted, january.AddMinutes(1));
        await DocumentSeed.DocumentAsync(company, 4, 1, DocumentStatus.Rejected, january.AddMinutes(2));
        // 1-ene-2026 a las 04:59 UTC todavía es 31-dic-2025 en Colombia.
        await DocumentSeed.DocumentAsync(company, 1, 3, DocumentStatus.Accepted, new DateTimeOffset(2026, 1, 1, 4, 59, 0, TimeSpan.Zero));
        await DocumentSeed.DocumentAsync(company, 5, 1, DocumentStatus.Accepted, new DateTimeOffset(2025, 12, 5, 15, 0, 0, TimeSpan.Zero));
        // Primer instante del primer mes de la serie (1-feb-2025 en Colombia) y el último que queda fuera.
        await DocumentSeed.DocumentAsync(company, 11, 1, DocumentStatus.Accepted, new DateTimeOffset(2025, 2, 1, 5, 0, 0, TimeSpan.Zero));
        await DocumentSeed.DocumentAsync(company, 1, 4, DocumentStatus.Accepted, new DateTimeOffset(2025, 2, 1, 4, 59, 59, TimeSpan.Zero));
        await DocumentSeed.DocumentAsync(other, 1, 1, DocumentStatus.Accepted, january);
        using var client = await DocumentSeed.ClientAsync(company);

        using var json = await DocumentSeed.GetJsonAsync(client, "/api/v1/dashboard/summary");

        var root = json.RootElement;
        var months = root.GetProperty("months").EnumerateArray().ToList();
        var current = root.GetProperty("current_month");
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("generated_at").GetDateTimeOffset(), Is.EqualTo(Now));
            Assert.That(current.GetProperty("month").GetString(), Is.EqualTo("2026-01"));
            Assert.That(current.GetProperty("total").GetInt64(), Is.EqualTo(3));
            Assert.That(current.GetProperty("by_status").EnumerateArray().Select(item => (item.GetProperty("status").GetString(), item.GetProperty("count").GetInt64())),
                Is.EqualTo(new[] { ("Accepted", 2L), ("Rejected", 1L) }));
            Assert.That(months.Select(month => month.GetProperty("month").GetString()), Is.EqualTo(new[]
            {
                "2025-02", "2025-03", "2025-04", "2025-05", "2025-06", "2025-07", "2025-08", "2025-09", "2025-10", "2025-11", "2025-12", "2026-01",
            }));
            Assert.That(Counts(months[11]), Is.EqualTo((2L, 1L, 0L, 0L, 3L)));
            Assert.That(Counts(months[10]), Is.EqualTo((1L, 0L, 1L, 0L, 2L)));
            Assert.That(Counts(months[0]), Is.EqualTo((0L, 0L, 0L, 1L, 1L)));
            Assert.That(months.Skip(1).Take(9).Select(Counts), Is.All.EqualTo((0L, 0L, 0L, 0L, 0L)));
        });
    }

    [Test]
    public async Task The_summary_shows_the_plan_usage_the_active_certificate_and_only_the_resolutions_running_out()
    {
        var company = await DocumentSeed.CompanyAsync();
        await PlanAsync(company.Id, PlanPeriod.Monthly, startsAt: new DateTimeOffset(2026, 1, 1, 5, 0, 0, TimeSpan.Zero), invoices: 100, used: 10);
        await CertificateAsync(company.Id, Now.AddDays(-400), active: false);
        await CertificateAsync(company.Id, Now.AddDays(20), active: true);
        var low = await ResolutionAsync(company.Id, "SETP", 1, 100, validTo: new DateOnly(2027, 1, 1), nextNumber: 95);
        await ResolutionAsync(company.Id, "FE", 1, 100, validTo: new DateOnly(2027, 1, 1), nextNumber: 10);
        var expiring = await ResolutionAsync(company.Id, "AB", 1, 1000, validTo: new DateOnly(2026, 1, 25), nextNumber: null);
        using var client = await DocumentSeed.ClientAsync(company);

        using var json = await DocumentSeed.GetJsonAsync(client, "/api/v1/dashboard/summary");

        var root = json.RootElement;
        var plan = root.GetProperty("plan");
        var quota = plan.GetProperty("quotas").EnumerateArray().Single();
        var certificate = root.GetProperty("certificate");
        var resolutions = root.GetProperty("resolutions").EnumerateArray().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(plan.GetProperty("period").GetString(), Is.EqualTo("monthly"));
            Assert.That(plan.GetProperty("is_active").GetBoolean(), Is.True);
            Assert.That(plan.GetProperty("period_end").GetDateTimeOffset(), Is.EqualTo(new DateTimeOffset(2026, 2, 1, 5, 0, 0, TimeSpan.Zero)));
            Assert.That(plan.GetProperty("expired").GetBoolean(), Is.False);
            Assert.That((quota.GetProperty("kind").GetString(), quota.GetProperty("used").GetInt64(), quota.GetProperty("limit").GetInt64(),
                quota.GetProperty("remaining").GetInt64()), Is.EqualTo(("invoice", 10L, 100L, 90L)));
            Assert.That(certificate.GetProperty("days_left").GetInt32(), Is.EqualTo(20));
            Assert.That(certificate.GetProperty("expired").GetBoolean(), Is.False);
            Assert.That(resolutions.Select(item => item.GetProperty("id").GetGuid()), Is.EquivalentTo(new[] { low, expiring }));
            var lowItem = resolutions.Single(item => item.GetProperty("id").GetGuid() == low);
            Assert.That((lowItem.GetProperty("remaining_numbers").GetInt64(), lowItem.GetProperty("numbers_low").GetBoolean(),
                lowItem.GetProperty("expiring").GetBoolean(), lowItem.GetProperty("kind").GetString()), Is.EqualTo((6L, true, false, "invoice")));
            var expiringItem = resolutions.Single(item => item.GetProperty("id").GetGuid() == expiring);
            Assert.That((expiringItem.GetProperty("days_left").GetInt32(), expiringItem.GetProperty("expiring").GetBoolean(),
                expiringItem.GetProperty("valid_to").GetString()), Is.EqualTo((10, true, "2026-01-25")));
        });
    }

    [Test]
    public async Task A_company_without_documents_plan_certificate_or_resolutions_gets_zeros_and_nulls()
    {
        var company = await DocumentSeed.CompanyAsync();
        var other = await DocumentSeed.CompanyAsync();
        await CertificateAsync(other.Id, Now.AddDays(5), active: true);
        await ResolutionAsync(other.Id, "SETP", 1, 100, validTo: null, nextNumber: 99);
        using var client = await DocumentSeed.ClientAsync(company);

        using var json = await DocumentSeed.GetJsonAsync(client, "/api/v1/dashboard/summary");

        var root = json.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("current_month").GetProperty("total").GetInt64(), Is.Zero);
            Assert.That(root.GetProperty("current_month").GetProperty("by_status").GetArrayLength(), Is.Zero);
            Assert.That(root.GetProperty("months").GetArrayLength(), Is.EqualTo(12));
            Assert.That(root.GetProperty("plan").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(root.GetProperty("certificate").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(root.GetProperty("resolutions").GetArrayLength(), Is.Zero);
        });
    }

    [Test]
    public async Task The_summary_needs_documents_read()
    {
        var company = await DocumentSeed.CompanyAsync();
        using var configOnly = await DocumentSeed.ClientAsync(company, ApiScopes.ConfigWrite);

        using var response = await configOnly.GetAsync("/api/v1/dashboard/summary");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    private static (long Invoice, long Credit, long Debit, long Other, long Total) Counts(JsonElement month) => (
        month.GetProperty("invoice").GetInt64(), month.GetProperty("credit_note").GetInt64(), month.GetProperty("debit_note").GetInt64(),
        month.GetProperty("other").GetInt64(), month.GetProperty("total").GetInt64());

    private static async Task PlanAsync(Guid companyId, PlanPeriod period, DateTimeOffset startsAt, long invoices, long used)
    {
        await using var scope = DocumentQueriesHost.Api.OpenScope(null, null, ActorType.System);
        var plan = new Plan { Name = "Tablero " + Guid.NewGuid().ToString("N"), Period = period };
        scope.Db.Add(plan);
        await scope.Db.SaveChangesAsync();
        var companyPlan = new CompanyPlan { CompanyId = companyId, PlanId = plan.Id, StartsAt = startsAt, IsActive = true };
        scope.Db.Add(new PlanQuota { PlanId = plan.Id, DocumentKind = DocumentKind.Invoice, Quantity = invoices });
        scope.Db.Add(companyPlan);
        await scope.Db.SaveChangesAsync();
        scope.Db.Add(new PlanUsage { CompanyPlanId = companyPlan.Id, DocumentKind = DocumentKind.Invoice, Used = used });
        await scope.Db.SaveChangesAsync();
    }

    private static async Task CertificateAsync(Guid companyId, DateTimeOffset notAfter, bool active)
    {
        await using var scope = DocumentQueriesHost.Api.OpenScope(null, null, ActorType.System);
        scope.Db.Add(new Certificate
        {
            CompanyId = companyId,
            Name = "firma-" + Guid.NewGuid().ToString("N")[..8] + ".p12",
            SecretRef = "certificates/" + Guid.NewGuid().ToString("N"),
            Thumbprint = Convert.ToHexString(Guid.NewGuid().ToByteArray()) + "00000000",
            Subject = "CN=Empresa",
            Issuer = "CN=Emisor",
            NotBefore = notAfter.AddYears(-1),
            NotAfter = notAfter,
            IsActive = active,
        });
        await scope.Db.SaveChangesAsync();
    }

    private static async Task<Guid> ResolutionAsync(Guid companyId, string prefix, long from, long to, DateOnly? validTo, long? nextNumber)
    {
        var resolution = new Resolution
        {
            Id = Guid.CreateVersion7(),
            CompanyId = companyId,
            DocumentTypeId = 1,
            TypeEnvironmentId = 2,
            Prefix = prefix,
            ResolutionNumber = "18760000001",
            ResolutionDate = new DateOnly(2025, 1, 1),
            TechnicalKey = "fc8eac422eba16e22ffd8c6f94b3f40a6e38162c",
            RangeFrom = from,
            RangeTo = to,
            ValidFrom = new DateOnly(2025, 1, 1),
            ValidTo = validTo,
            IsActive = true,
        };
        await using var scope = DocumentQueriesHost.Api.OpenScope(null, null, ActorType.System);
        scope.Db.Add(resolution);
        if (nextNumber is { } next)
        {
            scope.Db.Add(new NumberingSequence { CompanyId = companyId, ScopeKey = NumberingScope.ForResolution(resolution.Id), NextNumber = next });
        }

        await scope.Db.SaveChangesAsync();
        return resolution.Id;
    }
}
