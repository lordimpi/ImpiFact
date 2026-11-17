using ImpiFact.Application.Abstractions.Maintenance;
using ImpiFact.Application.Features.Maintenance;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImpiFact.Application.Tests.Features.Maintenance;

/// <summary>
/// Vigilante de certificados (Arquitectura detallada, sección 8): avisa a 30, 15 y 5 días del vencimiento, con
/// notificación y correo, una sola vez por umbral y certificado. Los días se cuentan en el calendario de Colombia.
/// </summary>
[TestFixture]
public sealed class CertificateExpiryWatcherTests
{
    /// <summary>2-mar-2026 a las 13:00 UTC, las 08:00 en Colombia.</summary>
    private static readonly DateTimeOffset Now = new(2026, 3, 2, 13, 0, 0, TimeSpan.Zero);

    private FakeExpiryWatchReads reads = null!;
    private FakeScheduledNotices notices = null!;

    [SetUp]
    public void SetUp()
    {
        reads = new FakeExpiryWatchReads();
        notices = new FakeScheduledNotices();
    }

    [TestCase(30, 30)]
    [TestCase(20, 30)]
    [TestCase(16, 30)]
    [TestCase(15, 15)]
    [TestCase(6, 15)]
    [TestCase(5, 5)]
    [TestCase(1, 5)]
    [TestCase(0, 5)]
    public async Task A_certificate_gets_the_notice_of_the_tightest_threshold_it_reached(int daysLeft, int threshold)
    {
        var certificate = Certificate(Now.AddDays(daysLeft).AddHours(1));

        await Watcher().RunAsync(CancellationToken.None);

        var notice = notices.Saved.Single();
        Assert.Multiple(() =>
        {
            Assert.That(notice.CompanyId, Is.EqualTo(certificate.CompanyId));
            Assert.That(notice.Key, Is.EqualTo($"certificate-expiry:{certificate.CertificateId:D}:{threshold}"));
            Assert.That(notice.Type, Is.EqualTo("CertificateExpiring"));
            Assert.That(notice.SendEmail, Is.True);
            Assert.That(notice.Body, Does.Contain("«firma.p12»"));
        });
    }

    [Test]
    public async Task A_certificate_more_than_30_days_away_gets_no_notice()
    {
        Certificate(Now.AddDays(31));

        await Watcher().RunAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(notices.Saved, Is.Empty);
            Assert.That(notices.Attempts, Is.Zero);
            Assert.That(reads.CertificateWindow?.Now, Is.EqualTo(Now));
            Assert.That(reads.CertificateWindow?.Until, Is.GreaterThanOrEqualTo(Now.AddDays(31)));
        });
    }

    [Test]
    public async Task Days_are_counted_in_the_colombian_calendar()
    {
        // 04:00 UTC del 2-mar son las 23:00 del 1-mar en Colombia: el 16-mar a las 13:00 UTC quedan 15 días, no 14.
        var watcher = Watcher(new DateTimeOffset(2026, 3, 2, 4, 0, 0, TimeSpan.Zero));
        var certificate = Certificate(new DateTimeOffset(2026, 3, 16, 13, 0, 0, TimeSpan.Zero));

        await watcher.RunAsync(CancellationToken.None);

        Assert.That(notices.Saved.Single().Key, Is.EqualTo($"certificate-expiry:{certificate.CertificateId:D}:15"));
    }

    [Test]
    public async Task The_notice_texts_are_in_spanish_with_the_days_left_and_the_expiry_date()
    {
        Certificate(new DateTimeOffset(2026, 3, 22, 13, 0, 0, TimeSpan.Zero));

        await Watcher().RunAsync(CancellationToken.None);

        var notice = notices.Saved.Single();
        Assert.Multiple(() =>
        {
            Assert.That(notice.Title, Is.EqualTo("El certificado de firma vence en 20 días"));
            Assert.That(notice.Body, Is.EqualTo(
                "El certificado de firma «firma.p12» vence el 22/03/2026 (fecha de Colombia). Después de esa fecha no se "
                + "pueden firmar ni enviar documentos a la DIAN. Suba el certificado nuevo desde el panel antes de que venza."));
        });
    }

    [TestCase(1, "El certificado de firma vence mañana")]
    [TestCase(0, "El certificado de firma vence hoy")]
    public async Task The_title_names_tomorrow_and_today(int daysLeft, string title)
    {
        Certificate(Now.AddDays(daysLeft).AddHours(1));

        await Watcher().RunAsync(CancellationToken.None);

        Assert.That(notices.Saved.Single().Title, Is.EqualTo(title));
    }

    [Test]
    public async Task Running_every_day_notifies_each_threshold_once()
    {
        var clock = new MaintenanceClock(Now);
        var certificate = Certificate(Now.AddDays(31));
        var watcher = new CertificateExpiryWatcher(reads, notices, clock, NullLogger<CertificateExpiryWatcher>.Instance);

        for (var day = 0; day <= 31; day++)
        {
            await watcher.RunAsync(CancellationToken.None);
            await watcher.RunAsync(CancellationToken.None);
            clock.UtcNow = clock.UtcNow.AddDays(1);
        }

        Assert.That(notices.Saved.Select(notice => notice.Key), Is.EqualTo(new[]
        {
            $"certificate-expiry:{certificate.CertificateId:D}:30",
            $"certificate-expiry:{certificate.CertificateId:D}:15",
            $"certificate-expiry:{certificate.CertificateId:D}:5",
        }));
    }

    [Test]
    public async Task A_failing_certificate_does_not_stop_the_others()
    {
        var first = Certificate(Now.AddDays(10));
        var second = Certificate(Now.AddDays(10));
        var failing = new FailingOnceNotices(notices, first.CertificateId);

        await new CertificateExpiryWatcher(reads, failing, new MaintenanceClock(Now), NullLogger<CertificateExpiryWatcher>.Instance)
            .RunAsync(CancellationToken.None);

        Assert.That(notices.Saved.Select(notice => notice.CompanyId), Is.EqualTo(new[] { second.CompanyId }));
    }

    private CertificateExpiryWatcher Watcher(DateTimeOffset? now = null) =>
        new(reads, notices, new MaintenanceClock(now ?? Now), NullLogger<CertificateExpiryWatcher>.Instance);

    private WatchedCertificate Certificate(DateTimeOffset notAfter)
    {
        var certificate = new WatchedCertificate(Guid.CreateVersion7(), Guid.CreateVersion7(), "firma.p12", notAfter);
        reads.Certificates.Add(certificate);
        return certificate;
    }

    /// <summary>Falla al guardar los avisos de un certificado y deja pasar los demás.</summary>
    private sealed class FailingOnceNotices(FakeScheduledNotices inner, Guid failingCertificate) : IScheduledNotices
    {
        public Task<bool> TryAddAsync(ScheduledNotice notice, CancellationToken ct) =>
            notice.Key.Contains(failingCertificate.ToString("D"), StringComparison.Ordinal)
                ? throw new InvalidOperationException("The database did not answer.")
                : inner.TryAddAsync(notice, ct);
    }
}
