using ImpiFact.Application.Abstractions.Maintenance;

namespace ImpiFact.Application.Tests.Features.Maintenance;

/// <summary>Regla compartida por el vigilante de resoluciones y el tablero: menos del 10 % del rango o menos de 30 días.</summary>
[TestFixture]
public sealed class ResolutionAlertRuleTests
{
    private static readonly DateOnly Today = new(2026, 3, 2);

    [TestCase(92L, 9L, true)]
    [TestCase(91L, 10L, false)]
    [TestCase(101L, 0L, true)]
    [TestCase(null, 100L, false)]
    [TestCase(-5L, 100L, false)]
    public void The_numbers_left_are_low_below_10_percent(long? nextNumber, long remaining, bool low)
    {
        var alert = ResolutionAlertRule.Evaluate(1, 100, nextNumber, validTo: null, Today);

        Assert.That((alert.Total, alert.Remaining, alert.NumbersLow, alert.Expiring, alert.DaysLeft), Is.EqualTo((100L, remaining, low, false, (int?)null)));
    }

    [TestCase(29, true, false)]
    [TestCase(30, false, false)]
    [TestCase(0, true, false)]
    [TestCase(-1, true, true)]
    public void The_validity_is_ending_below_30_days(int days, bool expiring, bool expired)
    {
        var alert = ResolutionAlertRule.Evaluate(1, 100, 10, Today.AddDays(days), Today);

        Assert.That((alert.DaysLeft, alert.Expiring, alert.Expired, alert.Any), Is.EqualTo(((int?)days, expiring, expired, expiring)));
    }
}
