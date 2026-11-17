using ImpiFact.Application.Abstractions.Maintenance;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Platform.Scheduling;

namespace ImpiFact.Integration.Tests.Scheduling;

/// <summary>
/// Los vigilantes diarios contra la base real (Arquitectura detallada, sección 8): el de certificados deja una
/// notificación y un correo a 30, 15 y 5 días del vencimiento; el de resoluciones, una notificación cuando queda menos
/// del 10 % del rango o faltan menos de 30 días para <c>valid_to</c>. Cada aviso sale una sola vez.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class ExpiryWatcherTests
{
    [Test]
    public async Task A_certificate_30_days_from_expiry_gets_one_notification_and_one_email()
    {
        await using var host = SchedulingHost.WithMailHog();
        var company = await host.CreateCompanyAsync();
        var certificate = await host.AddCertificateAsync(company.Id, host.Clock.UtcNow.AddDays(30));

        var run = await host.RunJobAsync("CertificateExpiryWatcher");
        var processed = await host.RunOutboxAsync();

        var notification = (await host.NotificationsAsync(company.Id)).Single();
        var message = (await host.NoticeEmailsAsync(company.Id)).Single();
        var email = (await SchedulingEnvironment.EmailsAsync(company.Name)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(ScheduledJobOutcome.Completed));
            Assert.That(processed, Is.GreaterThanOrEqualTo(1));
            Assert.That(notification.Id, Is.EqualTo(ScheduledNoticeIds.For($"certificate-expiry:{certificate.Id:D}:30")));
            Assert.That(notification.Type, Is.EqualTo("CertificateExpiring"));
            Assert.That(notification.Title, Is.EqualTo("El certificado de firma vence en 30 días"));
            Assert.That(notification.Body, Does.Contain($"«{certificate.Name}»").And.Contain("01/04/2026"));
            Assert.That(notification.UserId, Is.Null);
            Assert.That(notification.IsRead, Is.False);
            Assert.That(message.Status, Is.EqualTo(OutboxStatus.Done));
            Assert.That(message.Payload["notification_id"]?.GetValue<Guid>(), Is.EqualTo(notification.Id));
            Assert.That(email.EnvelopeFrom, Is.EqualTo(SchedulingHost.PlatformFrom));
            Assert.That(email.EnvelopeTo, Is.EqualTo(new[] { company.Email }));
            Assert.That(email.Message.Subject, Is.EqualTo($"{company.Name}: El certificado de firma vence en 30 días"));
            Assert.That(email.Message.TextBody, Does.Contain(notification.Body));
        });
    }

    [Test]
    public async Task Daily_runs_notify_the_30_15_and_5_day_thresholds_once_each()
    {
        await using var host = SchedulingHost.Create();
        var company = await host.CreateCompanyAsync();
        var certificate = await host.AddCertificateAsync(company.Id, host.Clock.UtcNow.AddDays(31));

        for (var day = 0; day <= 30; day++)
        {
            await host.RunJobAsync("CertificateExpiryWatcher");
            await host.RunJobAsync("CertificateExpiryWatcher");
            host.Clock.Advance(TimeSpan.FromDays(1));
        }

        var notifications = await host.NotificationsAsync(company.Id);
        Assert.Multiple(async () =>
        {
            Assert.That(notifications.Select(notification => notification.Id), Is.EquivalentTo(new[]
            {
                ScheduledNoticeIds.For($"certificate-expiry:{certificate.Id:D}:30"),
                ScheduledNoticeIds.For($"certificate-expiry:{certificate.Id:D}:15"),
                ScheduledNoticeIds.For($"certificate-expiry:{certificate.Id:D}:5"),
            }));
            Assert.That(await host.NoticeEmailsAsync(company.Id), Has.Count.EqualTo(3));
        });
    }

    [Test]
    public async Task Inactive_far_or_expired_certificates_get_no_notice()
    {
        await using var host = SchedulingHost.Create();
        var inactive = await host.CreateCompanyAsync();
        var far = await host.CreateCompanyAsync();
        var expired = await host.CreateCompanyAsync();
        await host.AddCertificateAsync(inactive.Id, host.Clock.UtcNow.AddDays(10), active: false);
        await host.AddCertificateAsync(far.Id, host.Clock.UtcNow.AddDays(31));
        await host.AddCertificateAsync(expired.Id, host.Clock.UtcNow.AddMinutes(-1));

        await host.RunJobAsync("CertificateExpiryWatcher");

        Assert.Multiple(async () =>
        {
            Assert.That(await host.NotificationsAsync(inactive.Id), Is.Empty);
            Assert.That(await host.NotificationsAsync(far.Id), Is.Empty);
            Assert.That(await host.NotificationsAsync(expired.Id), Is.Empty);
            Assert.That(await host.NoticeEmailsAsync(far.Id), Is.Empty);
        });
    }

    [Test]
    public async Task A_resolution_running_out_of_numbers_and_time_gets_two_notifications_without_email()
    {
        await using var host = SchedulingHost.Create();
        var company = await host.CreateCompanyAsync();
        var resolution = await host.AddResolutionAsync(company.Id, 1, 100, new DateOnly(2026, 3, 20), nextNumber: 95);

        await host.RunJobAsync("ResolutionRangeWatcher");
        await host.RunJobAsync("ResolutionRangeWatcher");

        var notifications = await host.NotificationsAsync(company.Id);
        Assert.Multiple(async () =>
        {
            Assert.That(notifications.Select(notification => (notification.Id, notification.Type)), Is.EquivalentTo(new[]
            {
                (ScheduledNoticeIds.For($"resolution-range:{resolution.Id:D}:numbers:100"), "ResolutionNumbersLow"),
                (ScheduledNoticeIds.For($"resolution-validity:{resolution.Id:D}:2026-03-20"), "ResolutionExpiring"),
            }));
            Assert.That(notifications.Single(notification => notification.Type == "ResolutionNumbersLow").Body,
                Does.StartWith("Quedan 6 de los 100 números autorizados"));
            Assert.That(await host.NoticeEmailsAsync(company.Id), Is.Empty);
        });
    }

    [Test]
    public async Task A_resolution_with_room_and_time_left_gets_no_notice()
    {
        await using var host = SchedulingHost.Create();
        var withSequence = await host.CreateCompanyAsync();
        var withoutSequence = await host.CreateCompanyAsync();
        await host.AddResolutionAsync(withSequence.Id, 1, 100, new DateOnly(2026, 4, 1), nextNumber: 91);
        await host.AddResolutionAsync(withoutSequence.Id, 1, 100, validTo: null, nextNumber: null);

        await host.RunJobAsync("ResolutionRangeWatcher");

        Assert.Multiple(async () =>
        {
            Assert.That(await host.NotificationsAsync(withSequence.Id), Is.Empty);
            Assert.That(await host.NotificationsAsync(withoutSequence.Id), Is.Empty);
        });
    }

    [Test]
    public async Task The_same_notice_key_saved_at_once_by_two_instances_is_stored_once()
    {
        await using var first = SchedulingHost.Create();
        await using var second = SchedulingHost.Create();
        var company = await first.CreateCompanyAsync();
        var notice = new ScheduledNotice(company.Id, "test:" + Guid.NewGuid(), "Test", "Título", "Texto", SendEmail: true);

        var saved = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => (index % 2 == 0 ? first : second).TryAddNoticeAsync(notice)));

        Assert.Multiple(async () =>
        {
            Assert.That(saved.Count(added => added), Is.EqualTo(1));
            Assert.That(await first.NotificationsAsync(company.Id), Has.Count.EqualTo(1));
            Assert.That(await first.NoticeEmailsAsync(company.Id), Has.Count.EqualTo(1));
        });
    }
}
