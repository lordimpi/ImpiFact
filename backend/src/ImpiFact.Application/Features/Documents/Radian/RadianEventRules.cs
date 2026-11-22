using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Documents.Radian;

/// <summary>Who registers the event: the buyer on a received invoice (030 to 033) or the seller on its own invoice (034).</summary>
public enum RadianTarget
{
    ReceivedInvoice,
    OwnInvoice,
}

/// <summary>An event already registered on the invoice (accepted by the DIAN): its code and the Colombian day it was issued.</summary>
public sealed record RegisteredEvent(string Code, DateOnly? Day);

/// <summary>
/// Order of the RADIAN events, checked before signing so the company gets a clear message instead of a DIAN rejection. The
/// PHP checks none (php-rules 16.3, item 3); the rules come from Anexo Técnico 1.9 (Resolución 000165 de 2023), the
/// primary source in reference/dian-anexo-tecnico:
/// <list type="bullet">
/// <item>LGC01 and LGC20: one event of each type per invoice.</item>
/// <item>LGC09: the 032 needs the 030.</item>
/// <item>LGC03: the claim (031) needs the 030 and the 032. LGC12 and LGC13: the express acceptance (033) needs the 032 and the 030.</item>
/// <item>LGC02, LGC04, LGC05, LGC07 and Tabla 8 (section 6.5.3): a claim and an acceptance exclude each other, and so do the
/// express and the tacit acceptance.</item>
/// <item>LGC14: the tacit acceptance (034) only after 3 business days from the 032, registered by the seller (section 6.5.1.4).</item>
/// </list>
/// The deadlines count business days (Decreto 1074 de 2015, article 2.2.2.53.4, modified by Decreto 1154 de 2020). The
/// Colombian holiday calendar is not in the repository, so the local check counts weekdays only: for the 034 that is a lower
/// bound (a holiday only makes the wait longer, and the DIAN rejects the rest); the 3-day limit of the express acceptance
/// (LGC06) is left to the DIAN, because counting without holidays would refuse valid acceptances (php-rules 18, question 7).
/// </summary>
public static class RadianEventRules
{
    private const int BusinessDays = 3;

    /// <param name="code">Event to register: 030 to 034.</param>
    /// <param name="registered">Events already accepted on the invoice (for an own invoice, those the DIAN lists).</param>
    /// <param name="today">Today in Colombia.</param>
    /// <returns>Null when the event may be registered; otherwise the error (HTTP 422).</returns>
    public static Error? Check(string code, RadianTarget target, IReadOnlyCollection<RegisteredEvent> registered, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(registered);
        if (target == RadianTarget.ReceivedInvoice && code == RadianCatalog.TacitAcceptance)
        {
            return RadianErrors.TacitOnlyForSeller;
        }

        if (target == RadianTarget.OwnInvoice && code != RadianCatalog.TacitAcceptance)
        {
            return RadianErrors.OnlyTacitOnOwnInvoice;
        }

        if (Has(registered, code))
        {
            return RadianErrors.AlreadyRegistered;
        }

        foreach (var previous in Previous(code))
        {
            if (!Has(registered, previous))
            {
                return RadianErrors.MissingPrevious(code, previous);
            }
        }

        foreach (var excluding in Excluding(code))
        {
            if (Has(registered, excluding))
            {
                return RadianErrors.Excluded(code, excluding);
            }
        }

        if (code == RadianCatalog.TacitAcceptance
            && registered.Where(e => e.Code == RadianCatalog.GoodsReceipt).Select(e => e.Day).FirstOrDefault() is { } receipt
            && today < FirstTacitAcceptanceDay(receipt))
        {
            return RadianErrors.TacitTooEarly(FirstTacitAcceptanceDay(receipt));
        }

        return null;
    }

    /// <summary>
    /// First day a 034 may be registered after a 032 issued on <paramref name="goodsReceipt"/>: the day after the third
    /// weekday that follows it (weekends do not count; holidays are not known here).
    /// </summary>
    public static DateOnly FirstTacitAcceptanceDay(DateOnly goodsReceipt)
    {
        var day = goodsReceipt;
        for (var counted = 0; counted < BusinessDays;)
        {
            day = day.AddDays(1);
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                counted++;
            }
        }

        return day.AddDays(1);
    }

    private static bool Has(IEnumerable<RegisteredEvent> registered, string code) => registered.Any(e => e.Code == code);

    private static string[] Previous(string code) => code switch
    {
        RadianCatalog.GoodsReceipt => [RadianCatalog.Acknowledgement],
        RadianCatalog.Claim or RadianCatalog.ExpressAcceptance => [RadianCatalog.Acknowledgement, RadianCatalog.GoodsReceipt],
        RadianCatalog.TacitAcceptance => [RadianCatalog.GoodsReceipt],
        _ => [],
    };

    private static string[] Excluding(string code) => code switch
    {
        RadianCatalog.Claim => [RadianCatalog.ExpressAcceptance, RadianCatalog.TacitAcceptance],
        RadianCatalog.ExpressAcceptance => [RadianCatalog.Claim, RadianCatalog.TacitAcceptance],
        RadianCatalog.TacitAcceptance => [RadianCatalog.Claim, RadianCatalog.ExpressAcceptance],
        _ => [],
    };
}
