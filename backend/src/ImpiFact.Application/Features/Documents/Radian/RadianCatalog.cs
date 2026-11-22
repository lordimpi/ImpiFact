using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Documents.Radian;

/// <summary>A RADIAN event of the <c>events</c> catalog (Infrastructure/Seed/events.json): its id, DIAN code and name.</summary>
public sealed record RadianEventType(int Id, string Code, string Name);

/// <summary>
/// The RADIAN events and claim reasons (php-rules 16.3; seeds <c>events.json</c> and <c>type_rejections.json</c>). They are
/// fixed by the DIAN (Anexo Técnico 1.9, section 14.4), so they live in code; the ids match the seeds because
/// <c>document_events.event_id</c> references the <c>events</c> table.
/// </summary>
public static class RadianCatalog
{
    public const string Acknowledgement = "030";
    public const string Claim = "031";
    public const string GoodsReceipt = "032";
    public const string ExpressAcceptance = "033";
    public const string TacitAcceptance = "034";

    public static readonly IReadOnlyList<RadianEventType> Events =
    [
        new(1, Acknowledgement, "Acuse de recibo de Factura Electrónica de Venta"),
        new(2, Claim, "Reclamo de la Factura Electrónica de Venta"),
        new(3, GoodsReceipt, "Recibo del bien y/o prestación del servicio"),
        new(4, ExpressAcceptance, "Aceptación expresa"),
        new(5, TacitAcceptance, "Aceptación Tácita"),
    ];

    /// <summary>Claim reasons of a 031 by code (<c>type_rejections</c>; the PHP's <c>type_rejection_id</c> is the id 1 to 4).</summary>
    public static readonly IReadOnlyList<(int Id, string Code, string Name)> ClaimReasons =
    [
        (1, "01", "Documento con inconsistencias"),
        (2, "02", "Mercancía no entregada totalmente"),
        (3, "03", "Mercancía no entregada parcialmente"),
        (4, "04", "Servicio no prestado"),
    ];

    public static RadianEventType? ByCode(string? code) => Events.FirstOrDefault(type => type.Code == code?.Trim());

    public static RadianEventType? ById(int id) => Events.FirstOrDefault(type => type.Id == id);

    public static string NameOf(string code) => ByCode(code)?.Name ?? code;

    /// <summary>
    /// Place of an event in the flow of Anexo Técnico 1.9 (030, 032, then 031 or 033, then 034): orders the events of an
    /// invoice issued in the same second.
    /// </summary>
    public static int SequenceOf(int eventId) => ById(eventId)?.Code switch
    {
        Acknowledgement => 1,
        GoodsReceipt => 2,
        Claim or ExpressAcceptance => 3,
        TacitAcceptance => 4,
        _ => 5,
    };

    /// <summary>The claim reason by its code (<c>01</c>) or by the PHP id (<c>1</c>); null if it is not one of the four.</summary>
    public static (int Id, string Code, string Name)? ClaimReason(string? value)
    {
        var text = value?.Trim();
        foreach (var reason in ClaimReasons)
        {
            if (reason.Code == text || reason.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) == text)
            {
                return reason;
            }
        }

        return null;
    }
}

/// <summary>Expected errors of RADIAN, with neutral Spanish messages (the PHP texts where the PHP has one).</summary>
public static class RadianErrors
{
    /// <summary>The received document does not exist in the company (also when it exists in another one).</summary>
    public static readonly Error ReceivedNotFound = new(
        "received_document.not_found", "No se encontró el documento recibido.", ErrorType.NotFound);

    /// <summary>php-rules 16.2, item 6.</summary>
    public static readonly Error AlreadyRegistered = new(
        "radian.event_already_registered", "Ya se registro este evento para este documento.", ErrorType.Validation);

    public static readonly Error SoftwareMissing = new(
        "software.missing", "La empresa no tiene configurado el software de facturación.", ErrorType.Validation);

    /// <summary>php-rules 16.7, item 4.</summary>
    public static readonly Error NotAnInvoice = new(
        "radian.not_an_invoice", "El CUFE ingresado no corresponde al XML de un documento Invoice.", ErrorType.Validation);

    public static Error WrongBuyer(string nit) => new(
        "radian.wrong_buyer", $"El adquiriente del documento no corresponde a la empresa con nit: {nit}", ErrorType.Validation);

    public static Error DianNotFound(string? message) => new(
        "radian.dian_document_not_found",
        "La DIAN no entregó el documento: " + (string.IsNullOrWhiteSpace(message) ? "sin detalle." : message.Trim()),
        ErrorType.Validation);

    public static readonly Error DianUnavailable = new(
        "dian.unavailable", "El servicio DIAN no se encuentra disponible en el momento, reintente mas tarde...", ErrorType.Unavailable);

    public static Error DianFault(string reason) => new(
        "dian.fault", "La DIAN rechazó la solicitud: " + reason, ErrorType.DianRejected);

    public static readonly Error UnknownEvent = Error.Validation(
        [new FieldError("event_code", "El evento debe ser 030, 031, 032, 033 o 034.")]);

    public static readonly Error ClaimReasonRequired = Error.Validation(
        [new FieldError("claim_reason", "El reclamo (031) requiere el motivo: 01, 02, 03 o 04.")]);

    public static readonly Error ClaimReasonNotAllowed = Error.Validation(
        [new FieldError("claim_reason", "El motivo solo se informa con el reclamo (031).")]);

    public static Error MissingPrevious(string code, string previous) => new(
        "radian.event_out_of_order",
        $"El evento {code} ({RadianCatalog.NameOf(code)}) requiere que antes se haya registrado el evento {previous} " +
        $"({RadianCatalog.NameOf(previous)}).",
        ErrorType.Validation);

    public static Error Excluded(string code, string existing) => new(
        "radian.event_out_of_order",
        $"No se puede registrar el evento {code} ({RadianCatalog.NameOf(code)}) porque el documento ya tiene el evento {existing} " +
        $"({RadianCatalog.NameOf(existing)}).",
        ErrorType.Validation);

    public static readonly Error TacitOnlyForSeller = new(
        "radian.event_not_allowed",
        "El evento 034 (Aceptación Tácita) lo registra el emisor sobre su propia factura, no el adquiriente.",
        ErrorType.Validation);

    public static readonly Error OnlyTacitOnOwnInvoice = new(
        "radian.event_not_allowed",
        "Sobre una factura propia solo se registra el evento 034 (Aceptación Tácita); los eventos 030 a 033 los registra el adquiriente.",
        ErrorType.Validation);

    public static Error TacitTooEarly(DateOnly firstDay) => new(
        "radian.tacit_acceptance_too_early",
        "La aceptación tácita (034) solo se puede registrar pasados 3 días hábiles desde el evento 032 (Recibo del bien y/o " +
        $"prestación del servicio): no antes del {firstDay:yyyy-MM-dd}, sin contar los festivos, que valida la DIAN.",
        ErrorType.Validation);

    /// <summary>Only accepted invoices of the company can be referenced by a 034.</summary>
    public static readonly Error OwnInvoiceNotAccepted = new(
        "radian.invoice_not_accepted",
        "La aceptación tácita solo se registra sobre una factura de venta propia aceptada por la DIAN.",
        ErrorType.Validation);
}
