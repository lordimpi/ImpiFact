using System.Net.Mail;

namespace ImpiFact.Application.Features.Documents.Email;

/// <summary>Por qué un documento aceptado no lleva correo.</summary>
internal enum EmailSkipReason
{
    /// <summary>La solicitud no pidió el correo (<c>sendmail</c>).</summary>
    NotRequested,

    /// <summary>El comprador es el consumidor final y la solicitud no trajo <c>email_pos_customer</c>.</summary>
    FinalConsumer,

    /// <summary>La solicitud pidió el correo, pero el comprador no tiene una dirección válida.</summary>
    NoRecipient,
}

/// <summary>Destinatarios del correo de un documento, o por qué no lleva.</summary>
/// <param name="Skip">Nulo cuando hay a quién enviarlo.</param>
internal sealed record EmailRecipients(EmailSkipReason? Skip, IReadOnlyList<string> To, IReadOnlyList<string> Bcc)
{
    public static EmailRecipients None(EmailSkipReason reason) => new(reason, [], []);
}

/// <summary>
/// A quién va el correo de un documento aceptado, con las reglas de ApiDian (InvoiceController, líneas 668 a 686).
/// </summary>
/// <remarks>
/// <para>
/// Solo si la solicitud lo pidió (<c>sendmail</c>). Nunca al consumidor final (identificación 222222222222) salvo que
/// llegue <c>email_pos_customer</c>; sin ella no sale ningún correo, tampoco las copias. Con
/// <c>email_pos_customer</c> el correo va a esa dirección en lugar de la del documento.
/// </para>
/// <para>
/// Diferencia con el PHP: el PHP envía un correo aparte a cada copia (<c>sendmailtome</c> y <c>email_cc_list</c>);
/// aquí sale un solo mensaje y las copias van ocultas (Bcc), así ningún destinatario ve las direcciones de los demás,
/// como en el PHP, y un reintento nunca repite el correo solo a una parte de ellos. Las direcciones inválidas o
/// repetidas de las copias se descartan. Las notas siguen la misma regla del consumidor final que las facturas (el
/// PHP solo la aplica en las facturas).
/// </para>
/// </remarks>
internal static class DocumentEmailRecipients
{
    /// <summary>Identificación del consumidor final (comprador sin identificar) en la DIAN.</summary>
    public const string FinalConsumerIdentification = "222222222222";

    /// <param name="customerIdentification">Identificación del comprador, de la copia del cliente en el documento.</param>
    /// <param name="customerEmail">Correo del comprador en el documento.</param>
    /// <param name="companyEmail">Correo de la empresa emisora, para <c>sendmailtome</c>.</param>
    public static EmailRecipients Resolve(
        DocumentEmailRequest request, string customerIdentification, string? customerEmail, string? companyEmail)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.SendMail)
        {
            return EmailRecipients.None(EmailSkipReason.NotRequested);
        }

        if (customerIdentification.Trim() == FinalConsumerIdentification && request.PosCustomerEmail is null)
        {
            return EmailRecipients.None(EmailSkipReason.FinalConsumer);
        }

        if (Normalize(request.PosCustomerEmail ?? customerEmail) is not { } to)
        {
            return EmailRecipients.None(EmailSkipReason.NoRecipient);
        }

        var candidates = new List<string?>();
        if (request.SendMailToMe)
        {
            candidates.Add(companyEmail);
        }

        candidates.AddRange(request.CcList);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { to };
        var copies = candidates
            .Select(Normalize)
            .OfType<string>()
            .Where(seen.Add)
            .ToList();
        return new EmailRecipients(null, [to], copies);
    }

    /// <summary>La dirección sin espacios alrededor si es válida; nula si no lo es.</summary>
    private static string? Normalize(string? address)
    {
        var trimmed = address?.Trim();
        return !string.IsNullOrEmpty(trimmed) && MailAddress.TryCreate(trimmed, out var parsed) && parsed.DisplayName.Length == 0
            ? parsed.Address
            : null;
    }
}
