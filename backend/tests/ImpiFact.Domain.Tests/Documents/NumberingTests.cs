using ImpiFact.Domain.Documents;

namespace ImpiFact.Domain.Tests.Documents;

/// <summary>
/// Reglas de numeración (Modelo de datos detallado, sección 5; php-rules, sección 3, regla 3): la clave del
/// consecutivo por resolución, el rango y la vigencia de la resolución, y que el consecutivo nunca baja.
/// </summary>
public sealed class NumberingTests
{
    private static readonly NumberingRange Range = new(
        From: 990000000, To: 995000000, ValidFrom: new DateOnly(2019, 1, 19), ValidTo: new DateOnly(2030, 1, 19), IsActive: true);

    private static readonly DateOnly InValidity = new(2026, 1, 15);

    [Test]
    public void The_scope_key_of_a_resolution_is_r_and_its_id()
    {
        var resolutionId = Guid.Parse("0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b");

        Assert.That(NumberingScope.ForResolution(resolutionId), Is.EqualTo("R:0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b"));
    }

    [Test]
    public void The_scope_key_without_resolution_uses_type_environment_prefix_and_year()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NumberingScope.ForDocumentType(11, 2, "DS", 2026), Is.EqualTo("T:11:2:DS:2026"));
            Assert.That(NumberingScope.ForDocumentType(9, 1, null, 2026), Is.EqualTo("T:9:1::2026"));
        });
    }

    [TestCase(990000000L, TestName = "The_first_number_of_the_range_is_valid")]
    [TestCase(992500000L, TestName = "A_number_inside_the_range_is_valid")]
    [TestCase(995000000L, TestName = "The_last_number_of_the_range_is_valid")]
    public void A_number_inside_the_range_and_the_validity_is_valid(long number)
    {
        Assert.That(Range.Check(number, InValidity), Is.EqualTo(NumberingCheck.Valid));
    }

    [TestCase(989999999L)]
    [TestCase(995000001L)]
    public void A_number_outside_the_range_is_rejected(long number)
    {
        Assert.That(Range.Check(number, InValidity), Is.EqualTo(NumberingCheck.OutOfRange));
    }

    [Test]
    public void The_validity_days_are_inclusive()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Range.Check(990000000, new DateOnly(2019, 1, 19)), Is.EqualTo(NumberingCheck.Valid));
            Assert.That(Range.Check(990000000, new DateOnly(2030, 1, 19)), Is.EqualTo(NumberingCheck.Valid));
        });
    }

    [Test]
    public void A_date_before_the_validity_is_not_yet_valid()
    {
        Assert.That(Range.Check(990000000, new DateOnly(2019, 1, 18)), Is.EqualTo(NumberingCheck.NotYetValid));
    }

    [Test]
    public void A_date_after_the_validity_is_expired()
    {
        Assert.That(Range.Check(990000000, new DateOnly(2030, 1, 20)), Is.EqualTo(NumberingCheck.Expired));
    }

    [Test]
    public void A_resolution_without_validity_dates_only_checks_its_range()
    {
        var noteRange = new NumberingRange(From: 1, To: 99999999, ValidFrom: null, ValidTo: null, IsActive: true);

        Assert.Multiple(() =>
        {
            Assert.That(noteRange.Check(1, new DateOnly(1999, 1, 1)), Is.EqualTo(NumberingCheck.Valid));
            Assert.That(noteRange.Check(100000000, new DateOnly(1999, 1, 1)), Is.EqualTo(NumberingCheck.OutOfRange));
        });
    }

    [Test]
    public void An_inactive_resolution_rejects_every_number()
    {
        Assert.That((Range with { IsActive = false }).Check(990000000, InValidity), Is.EqualTo(NumberingCheck.Inactive));
    }

    [Test]
    public void A_sequence_starts_at_the_given_number()
    {
        Assert.That(NewSequence(990000000).NextNumber, Is.EqualTo(990000000));
    }

    [Test]
    public void Raising_a_sequence_moves_it_forward()
    {
        var sequence = NewSequence(10);

        sequence.RaiseTo(25);

        Assert.That(sequence.NextNumber, Is.EqualTo(25));
    }

    [Test]
    public void Raising_a_sequence_to_a_lower_number_keeps_it_where_it_was()
    {
        var sequence = NewSequence(25);

        sequence.RaiseTo(10);

        Assert.That(sequence.NextNumber, Is.EqualTo(25), "A sequence never goes back.");
    }

    private static NumberingSequence NewSequence(long next) => new() { ScopeKey = "R:x", NextNumber = next };
}
