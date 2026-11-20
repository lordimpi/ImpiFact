using ImpiFact.Application.Abstractions.Dian;
using ImpiFact.Dian.Soap;
using ImpiFact.Infrastructure.Observability;
using ImpiFact.Integration.Tests.Persistence;

namespace ImpiFact.Integration.Tests.Observability;

/// <summary>
/// Cuándo la DIAN queda «degradada» en <c>/health/ready</c>: al menos tres llamadas en los últimos cinco minutos y más de
/// la mitad sin respuesta o con un error del servidor que no es un SOAP Fault.
/// </summary>
[TestFixture]
public sealed class DianCallMonitorTests
{
    private readonly FixedClock clock = new(new DateTimeOffset(2026, 3, 2, 13, 0, 0, TimeSpan.Zero));

    [Test]
    public void Two_failed_calls_are_not_enough_to_report_it_degraded()
    {
        var monitor = new DianCallMonitor(clock);

        monitor.Record(NotSent());
        monitor.Record(NotSent());

        Assert.That(monitor.IsDegraded(), Is.False);
    }

    [Test]
    public void Most_of_the_recent_calls_without_an_answer_report_it_degraded()
    {
        var monitor = new DianCallMonitor(clock);

        monitor.Record(Answered(200));
        monitor.Record(NotSent());
        monitor.Record(new DianCall(DianCallOutcome.NoAnswer, "", null, null, null, TimeSpan.FromSeconds(60), "Timeout", PossiblyReceived: true));

        Assert.Multiple(() =>
        {
            Assert.That(monitor.Recent(), Is.EqualTo((3, 2)));
            Assert.That(monitor.IsDegraded(), Is.True);
        });
    }

    [Test]
    public void A_server_error_counts_as_a_failure_but_a_soap_fault_is_an_answer()
    {
        var monitor = new DianCallMonitor(clock);

        monitor.Record(Answered(503));
        monitor.Record(Answered(500, new DianSoapFaultResponse("s:Sender", null, "Invalid signature")));
        monitor.Record(Answered(500, new DianSoapFaultResponse("s:Sender", null, "Invalid signature")));

        Assert.Multiple(() =>
        {
            Assert.That(monitor.Recent(), Is.EqualTo((3, 1)));
            Assert.That(monitor.IsDegraded(), Is.False);
        });
    }

    [Test]
    public void Failures_older_than_five_minutes_no_longer_count()
    {
        var monitor = new DianCallMonitor(clock);
        monitor.Record(NotSent());
        monitor.Record(NotSent());
        monitor.Record(NotSent());

        clock.Advance(DianCallMonitor.Window + TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(monitor.Recent(), Is.EqualTo((0, 0)));
            Assert.That(monitor.IsDegraded(), Is.False);
        });
    }

    private static DianCall NotSent() => new(DianCallOutcome.NotSent, "", null, null, null, TimeSpan.Zero, "Connection refused");

    private static DianCall Answered(int status, DianSoapResponse? response = null) =>
        new(DianCallOutcome.Answered, "", status, "<body/>", response, TimeSpan.FromSeconds(1), null);
}
