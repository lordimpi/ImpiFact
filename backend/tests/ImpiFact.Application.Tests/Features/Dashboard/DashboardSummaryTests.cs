using ImpiFact.Application.Features.Dashboard;
using ImpiFact.Application.Tests.Features.Documents;
using ImpiFact.Application.Tests.Features.Maintenance;
using ImpiFact.Domain.Common;

namespace ImpiFact.Application.Tests.Features.Dashboard;

/// <summary>Armado del tablero: meses de Colombia, estados del mes, consumo del plan, certificado y resoluciones.</summary>
public sealed class DashboardSummaryTests
{
    /// <summary>1-mar-2026 a las 03:00 UTC: todavía es 28-feb-2026 en Colombia.</summary>
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 3, 0, 0, TimeSpan.Zero);

    private FakeDashboardReads _reads = null!;
    private Guid _companyId;

    [SetUp]
    public void SetUp()
    {
        _companyId = Guid.CreateVersion7();
        _reads = new FakeDashboardReads();
    }

    [Test]
    public async Task The_series_has_twelve_colombian_months_and_asks_for_their_utc_range_in_one_read()
    {
        _reads.Counts.AddRange(
        [
            new DocumentCountRow("2026-02", 1, "invoice", "Accepted", 5),
            new DocumentCountRow("2026-02", 1, "invoice", "Rejected", 1),
            new DocumentCountRow("2026-02", 4, "credit_note", "Accepted", 2),
            new DocumentCountRow("2026-02", 11, null, "Queued", 3),
            new DocumentCountRow("2025-03", 5, "debit_note", "Accepted", 4),
        ]);

        var summary = (await Handler().HandleAsync(new GetDashboardSummaryQuery(), CancellationToken.None)).Value;

        Assert.Multiple(() =>
        {
            Assert.That(_reads.CountCalls, Is.EqualTo(new[]
            {
                (_companyId, new DateTimeOffset(2025, 3, 1, 5, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 3, 1, 5, 0, 0, TimeSpan.Zero)),
            }));
            Assert.That(summary.CurrentMonth.Month, Is.EqualTo("2026-02"));
            Assert.That(summary.CurrentMonth.Total, Is.EqualTo(11));
            Assert.That(summary.CurrentMonth.ByStatus.Select(item => (item.Status, item.Count)),
                Is.EqualTo(new[] { ("Accepted", 7L), ("Queued", 3L), ("Rejected", 1L) }));
            Assert.That(summary.Months, Has.Count.EqualTo(12));
            Assert.That(summary.Months[0].Month, Is.EqualTo("2025-03"));
            Assert.That(summary.Months[0].DebitNote, Is.EqualTo(4));
            Assert.That(summary.Months[11], Is.EqualTo(new DashboardMonthResponse
            {
                Month = "2026-02", Invoice = 6, CreditNote = 2, DebitNote = 0, Other = 3, Total = 11,
            }));
        });
    }

    [Test]
    public async Task The_plan_shows_used_limit_and_remaining_and_an_unlimited_plan_has_no_limit()
    {
        _reads.Plan = new DashboardPlan("Plan Pyme", "monthly", true, Now.AddDays(-40), Now.AddDays(-10),
            [new DashboardQuota(DocumentKind.Invoice, 120, 100), new DashboardQuota(DocumentKind.Payroll, 3, null)]);

        var plan = (await Handler().HandleAsync(new GetDashboardSummaryQuery(), CancellationToken.None)).Value.Plan!;

        Assert.Multiple(() =>
        {
            Assert.That(plan.Expired, Is.True);
            Assert.That(plan.Quotas.Select(quota => (quota.Kind, quota.Used, quota.Limit, quota.Remaining)),
                Is.EqualTo(new[] { ("invoice", 120L, (long?)100, (long?)0), ("payroll", 3L, (long?)null, (long?)null) }));
        });
    }

    [Test]
    public async Task The_certificate_days_are_counted_in_the_colombian_calendar()
    {
        // 2-mar-2026 a las 04:00 UTC es todavía 1-mar en Colombia: un día después del 28-feb.
        _reads.Certificate = new DashboardCertificate("firma.p12", new DateTimeOffset(2026, 3, 2, 4, 0, 0, TimeSpan.Zero));

        var certificate = (await Handler().HandleAsync(new GetDashboardSummaryQuery(), CancellationToken.None)).Value.Certificate!;

        Assert.That((certificate.Name, certificate.DaysLeft, certificate.Expired), Is.EqualTo(("firma.p12", 1, false)));
    }

    [Test]
    public async Task Only_resolutions_running_out_are_listed_with_the_watcher_rule()
    {
        var low = Resolution("SETP", nextNumber: 95, validTo: new DateOnly(2027, 1, 1));
        var expired = Resolution("FE", nextNumber: 10, validTo: new DateOnly(2026, 2, 27));
        _reads.Resolutions.AddRange([low, Resolution("AB", nextNumber: 10, validTo: new DateOnly(2026, 3, 30)), expired]);

        var resolutions = (await Handler().HandleAsync(new GetDashboardSummaryQuery(), CancellationToken.None)).Value.Resolutions;

        Assert.Multiple(() =>
        {
            Assert.That(resolutions.Select(item => item.Id), Is.EqualTo(new[] { expired.Id, low.Id }));
            Assert.That((resolutions[0].DaysLeft, resolutions[0].Expiring, resolutions[0].Expired, resolutions[0].ValidTo),
                Is.EqualTo(((int?)-1, true, true, "2026-02-27")));
            Assert.That((resolutions[1].RemainingNumbers, resolutions[1].NumbersLow, resolutions[1].Expiring), Is.EqualTo((6L, true, false)));
        });
    }

    [Test]
    public async Task Without_plan_certificate_or_resolutions_they_are_null_and_empty()
    {
        var summary = (await Handler().HandleAsync(new GetDashboardSummaryQuery(), CancellationToken.None)).Value;

        Assert.That((summary.Plan, summary.Certificate, summary.Resolutions.Count, summary.Months.Sum(month => month.Total)),
            Is.EqualTo(((DashboardPlanResponse?)null, (DashboardCertificateResponse?)null, 0, 0L)));
    }

    private static DashboardResolution Resolution(string prefix, long? nextNumber, DateOnly? validTo) =>
        new(Guid.CreateVersion7(), 1, "invoice", prefix, "18760000001", 1, 100, nextNumber, validTo);

    private GetDashboardSummaryHandler Handler() => new(_reads, new FakeDocumentTenant(_companyId), new MaintenanceClock(Now));
}

public sealed class FakeDashboardReads : IDashboardReads
{
    public List<DocumentCountRow> Counts { get; } = [];

    public List<(Guid CompanyId, DateTimeOffset From, DateTimeOffset Until)> CountCalls { get; } = [];

    public DashboardPlan? Plan { get; set; }

    public DashboardCertificate? Certificate { get; set; }

    public List<DashboardResolution> Resolutions { get; } = [];

    public Task<IReadOnlyList<DocumentCountRow>> CountDocumentsAsync(Guid companyId, DateTimeOffset from, DateTimeOffset until, CancellationToken ct)
    {
        CountCalls.Add((companyId, from, until));
        return Task.FromResult<IReadOnlyList<DocumentCountRow>>(Counts);
    }

    public Task<DashboardPlan?> FindCurrentPlanAsync(Guid companyId, CancellationToken ct) => Task.FromResult(Plan);

    public Task<DashboardCertificate?> FindActiveCertificateAsync(Guid companyId, CancellationToken ct) => Task.FromResult(Certificate);

    public Task<IReadOnlyList<DashboardResolution>> ListActiveResolutionsAsync(Guid companyId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DashboardResolution>>(Resolutions);
}
