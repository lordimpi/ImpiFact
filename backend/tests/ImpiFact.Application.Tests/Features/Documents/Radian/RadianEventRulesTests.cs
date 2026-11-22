using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Radian;

namespace ImpiFact.Application.Tests.Features.Documents.Radian;

/// <summary>
/// Order of the RADIAN events (phase 7a, T6), from the logic rules LGC01 to LGC14 of Anexo Técnico 1.9, section 8.6, and
/// the matrix of section 6.5.3 (Tabla 8). The PHP enforces none of them (php-rules 16.3, item 3).
/// </summary>
public sealed class RadianEventRulesTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);

    [Test]
    public void The_receipt_acknowledgement_is_the_first_event_of_a_received_invoice() =>
        Assert.That(Check("030"), Is.Null);

    [TestCase("030")]
    [TestCase("032")]
    [TestCase("033")]
    public void An_event_already_registered_cannot_be_sent_again(string code)
    {
        var error = Check(code, Registered("030", "032", "033"));

        Assert.That(error?.Message, Is.EqualTo("Ya se registro este evento para este documento."));
        Assert.That(error?.Type, Is.EqualTo(ErrorType.Validation));
    }

    [Test]
    public void The_receipt_of_the_goods_needs_the_acknowledgement_first()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Check("032")?.Message, Is.EqualTo(
                "El evento 032 (Recibo del bien y/o prestación del servicio) requiere que antes se haya registrado el evento 030 " +
                "(Acuse de recibo de Factura Electrónica de Venta)."));
            Assert.That(Check("032", Registered("030")), Is.Null);
        });
    }

    [TestCase("031")]
    [TestCase("033")]
    public void A_claim_or_an_express_acceptance_needs_the_acknowledgement_and_the_receipt_of_the_goods(string code)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Check(code)?.Message, Does.Contain("requiere que antes se haya registrado el evento 030"));
            Assert.That(Check(code, Registered("030"))?.Message, Does.Contain("requiere que antes se haya registrado el evento 032"));
            Assert.That(Check(code, Registered("030", "032")), Is.Null);
        });
    }

    [Test]
    public void A_claim_and_an_acceptance_exclude_each_other()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Check("031", Registered("030", "032", "033"))?.Message, Is.EqualTo(
                "No se puede registrar el evento 031 (Reclamo de la Factura Electrónica de Venta) porque el documento ya tiene el " +
                "evento 033 (Aceptación expresa)."));
            Assert.That(Check("033", Registered("030", "032", "031"))?.Message, Does.Contain("ya tiene el evento 031"));
            Assert.That(Check("031", Registered("030", "032", "034"))?.Message, Does.Contain("ya tiene el evento 034"));
            Assert.That(Check("033", Registered("030", "032", "034"))?.Message, Does.Contain("ya tiene el evento 034"));
        });
    }

    [Test]
    public void The_tacit_acceptance_is_only_for_the_seller_on_its_own_invoice()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Check("034", Registered("030", "032"))?.Message, Is.EqualTo(
                "El evento 034 (Aceptación Tácita) lo registra el emisor sobre su propia factura, no el adquiriente."));
            Assert.That(RadianEventRules.Check("030", RadianTarget.OwnInvoice, [], Monday)?.Message, Is.EqualTo(
                "Sobre una factura propia solo se registra el evento 034 (Aceptación Tácita); los eventos 030 a 033 los registra el " +
                "adquiriente."));
        });
    }

    [Test]
    public void The_tacit_acceptance_needs_the_receipt_of_the_goods_and_no_claim_nor_express_acceptance()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Own("034", Monday.AddDays(10))?.Message, Does.Contain("requiere que antes se haya registrado el evento 032"));
            Assert.That(Own("034", Monday.AddDays(10), ("030", Monday), ("032", Monday), ("031", Monday))?.Message, Does.Contain("ya tiene el evento 031"));
            Assert.That(Own("034", Monday.AddDays(10), ("030", Monday), ("032", Monday), ("033", Monday))?.Message, Does.Contain("ya tiene el evento 033"));
            Assert.That(Own("034", Monday.AddDays(10), ("030", Monday), ("032", Monday), ("034", Monday))?.Message,
                Is.EqualTo("Ya se registro este evento para este documento."));
        });
    }

    // LGC14: three business days after the 032. Weekends never count; holidays are left to the DIAN, so the local check
    // only refuses what is surely too early.
    [TestCase("2026-11-16", "2026-11-19", false, TestName = "Monday 032: Thursday is still the third business day")]
    [TestCase("2026-11-16", "2026-11-20", true, TestName = "Monday 032: Friday is after the three business days")]
    [TestCase("2026-11-19", "2026-11-24", false, TestName = "Thursday 032: the weekend does not count")]
    [TestCase("2026-11-19", "2026-11-25", true, TestName = "Thursday 032: Wednesday is after Friday, Monday and Tuesday")]
    public void The_tacit_acceptance_waits_three_business_days_after_the_receipt_of_the_goods(string receipt, string today, bool allowed)
    {
        var error = Own("034", DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture),
            ("030", DateOnly.Parse(receipt, System.Globalization.CultureInfo.InvariantCulture)),
            ("032", DateOnly.Parse(receipt, System.Globalization.CultureInfo.InvariantCulture)));

        if (allowed)
        {
            Assert.That(error, Is.Null);
        }
        else
        {
            Assert.That(error?.Message, Does.StartWith("La aceptación tácita (034) solo se puede registrar pasados 3 días hábiles"));
        }
    }

    [Test]
    public void The_first_allowed_day_of_the_tacit_acceptance_skips_weekends() =>
        Assert.That(RadianEventRules.FirstTacitAcceptanceDay(new DateOnly(2026, 11, 19)), Is.EqualTo(new DateOnly(2026, 11, 25)));

    private static Error? Check(string code, params RegisteredEvent[] registered) =>
        RadianEventRules.Check(code, RadianTarget.ReceivedInvoice, registered, Monday);

    private static Error? Own(string code, DateOnly today, params (string Code, DateOnly Day)[] registered) =>
        RadianEventRules.Check(code, RadianTarget.OwnInvoice, registered.Select(e => new RegisteredEvent(e.Code, e.Day)).ToList(), today);

    private static RegisteredEvent[] Registered(params string[] codes) => codes.Select(code => new RegisteredEvent(code, Monday)).ToArray();
}
