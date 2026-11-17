using ImpiFact.Application.Abstractions.Maintenance;
using ImpiFact.Application.Features.Maintenance;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImpiFact.Application.Tests.Features.Maintenance;

/// <summary>
/// Vigilante de resoluciones (Arquitectura detallada, sección 8): avisa cuando queda menos del 10 % del rango o faltan
/// menos de 30 días para <c>valid_to</c>, una sola vez por resolución y condición. Solo notificación, sin correo.
/// </summary>
[TestFixture]
public sealed class ResolutionRangeWatcherTests
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

    [TestCase(92L, true)]
    [TestCase(91L, false)]
    [TestCase(101L, true)]
    [TestCase(null, false)]
    public async Task Less_than_10_percent_of_the_range_left_is_notified_once(long? nextNumber, bool notified)
    {
        var resolution = Resolution(1, 100, nextNumber, validTo: null);

        await Watcher().RunAsync(CancellationToken.None);
        await Watcher().RunAsync(CancellationToken.None);

        Assert.That(notices.Saved.Select(notice => notice.Key),
            notified ? Is.EqualTo(new[] { $"resolution-range:{resolution.ResolutionId:D}:numbers:100" }) : Is.Empty);
    }

    [TestCase(29, true)]
    [TestCase(30, false)]
    [TestCase(-1, true)]
    public async Task Less_than_30_days_to_valid_to_is_notified_once(int days, bool notified)
    {
        var validTo = new DateOnly(2026, 3, 2).AddDays(days);
        var resolution = Resolution(1, 100, nextNumber: 10, validTo);

        await Watcher().RunAsync(CancellationToken.None);
        await Watcher().RunAsync(CancellationToken.None);

        Assert.That(notices.Saved.Select(notice => notice.Key),
            notified ? Is.EqualTo(new[] { $"resolution-validity:{resolution.ResolutionId:D}:{validTo:yyyy-MM-dd}" }) : Is.Empty);
    }

    [Test]
    public async Task Both_conditions_give_two_notices_for_the_company_without_email()
    {
        var resolution = Resolution(990000000, 995000000, nextNumber: 994800000, new DateOnly(2026, 3, 20));

        await Watcher().RunAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(notices.Saved.Select(notice => notice.Type), Is.EqualTo(new[] { "ResolutionNumbersLow", "ResolutionExpiring" }));
            Assert.That(notices.Saved.Select(notice => notice.CompanyId), Is.All.EqualTo(resolution.CompanyId));
            Assert.That(notices.Saved.Select(notice => notice.SendEmail), Is.All.False);
        });
    }

    [Test]
    public async Task The_notice_texts_are_in_spanish_with_the_numbers_left_and_the_end_date()
    {
        Resolution(990000000, 995000000, nextNumber: 994800000, new DateOnly(2026, 3, 20));

        await Watcher().RunAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(notices.Saved[0].Title, Is.EqualTo("Quedan pocos números en la resolución 18760000001 con prefijo SETP"));
            Assert.That(notices.Saved[0].Body, Is.EqualTo(
                "Quedan 200001 de los 5000001 números autorizados (del 990000000 al 995000000). Solicite a la DIAN una "
                + "nueva resolución de numeración y regístrela antes de que se agoten."));
            Assert.That(notices.Saved[1].Title, Is.EqualTo("La resolución 18760000001 con prefijo SETP vence pronto"));
            Assert.That(notices.Saved[1].Body, Is.EqualTo(
                "La vigencia de la resolución 18760000001 con prefijo SETP termina el 20/03/2026. Solicite a la DIAN una "
                + "nueva resolución de numeración y regístrela antes de esa fecha."));
        });
    }

    [Test]
    public async Task Used_up_and_expired_resolutions_say_so()
    {
        Resolution(1, 100, nextNumber: 101, new DateOnly(2026, 3, 1), resolutionNumber: null, prefix: "NC");

        await Watcher().RunAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(notices.Saved[0].Title, Is.EqualTo("Se agotaron los números de la resolución con prefijo NC"));
            Assert.That(notices.Saved[1].Title, Is.EqualTo("La resolución con prefijo NC está vencida"));
            Assert.That(notices.Saved[1].Body, Does.StartWith("La vigencia de la resolución con prefijo NC terminó el 01/03/2026."));
        });
    }

    [Test]
    public async Task A_new_range_or_validity_of_the_same_resolution_is_notified_again()
    {
        var resolution = Resolution(1, 100, nextNumber: 97, new DateOnly(2026, 3, 20));
        await Watcher().RunAsync(CancellationToken.None);

        reads.Resolutions[0] = resolution with { RangeTo = 105, ValidTo = new DateOnly(2026, 3, 25) };
        await Watcher().RunAsync(CancellationToken.None);

        Assert.That(notices.Saved.Select(notice => notice.Key), Is.EqualTo(new[]
        {
            $"resolution-range:{resolution.ResolutionId:D}:numbers:100",
            $"resolution-validity:{resolution.ResolutionId:D}:2026-03-20",
            $"resolution-range:{resolution.ResolutionId:D}:numbers:105",
            $"resolution-validity:{resolution.ResolutionId:D}:2026-03-25",
        }));
    }

    private ResolutionRangeWatcher Watcher() =>
        new(reads, notices, new MaintenanceClock(Now), NullLogger<ResolutionRangeWatcher>.Instance);

    private WatchedResolution Resolution(
        long from, long to, long? nextNumber, DateOnly? validTo, string? resolutionNumber = "18760000001", string? prefix = "SETP")
    {
        var resolution = new WatchedResolution(
            Guid.CreateVersion7(), Guid.CreateVersion7(), 1, prefix, resolutionNumber, from, to, nextNumber, validTo);
        reads.Resolutions.Add(resolution);
        return resolution;
    }
}
