using ImpiFact.Application.Features.Documents.Delivery;

namespace ImpiFact.Application.Tests.Features.Documents.Delivery;

/// <summary>
/// Cuándo se vuelve a consultar en la DIAN un documento sin confirmar: «cada 1 minuto durante ~10 minutos y luego cada
/// 15 minutos hasta 24 h; pasado ese plazo pasa a <c>Failed</c> con alerta» (Arquitectura detallada, sección 7).
/// </summary>
public sealed class DianPollScheduleTests
{
    private static readonly DateTimeOffset SentAt = new(2026, 3, 2, 8, 0, 0, TimeSpan.Zero);

    [TestCase(0)]
    [TestCase(5)]
    [TestCase(9)]
    public void During_the_first_10_minutes_it_polls_every_minute(int minutesSinceSent)
    {
        var now = SentAt.AddMinutes(minutesSinceSent);

        Assert.That(DianPollSchedule.Next(SentAt, now), Is.EqualTo(now.AddMinutes(1)));
    }

    [TestCase(10)]
    [TestCase(60)]
    [TestCase(23 * 60)]
    public void After_10_minutes_it_polls_every_15_minutes(int minutesSinceSent)
    {
        var now = SentAt.AddMinutes(minutesSinceSent);

        Assert.That(DianPollSchedule.Next(SentAt, now), Is.EqualTo(now.AddMinutes(15)));
    }

    [Test]
    public void The_last_poll_is_exactly_24_hours_after_the_send()
    {
        var now = SentAt.AddHours(24).AddMinutes(-5);

        Assert.That(DianPollSchedule.Next(SentAt, now), Is.EqualTo(SentAt.AddHours(24)));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void From_24_hours_on_there_is_no_next_poll(int minutesAfterDeadline)
    {
        var now = SentAt.AddHours(24).AddMinutes(minutesAfterDeadline);

        Assert.That(DianPollSchedule.Next(SentAt, now), Is.Null);
    }
}
