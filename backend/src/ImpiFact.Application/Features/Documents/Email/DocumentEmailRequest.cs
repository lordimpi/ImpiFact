using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ImpiFact.Application.Features.Documents.Email;

/// <summary>
/// Lo que la solicitud de emisión pidió del correo, leído de la copia guardada (<c>document_files</c> de tipo
/// solicitud). Los nombres de los campos son los del contrato de ApiDian (la implementación de referencia en PHP).
/// </summary>
/// <remarks>
/// Los datos del servidor de correo de la solicitud (<c>smtp_parameters</c>) no se guardan, porque traen la contraseña
/// en claro: por eso el envío diferido no puede usarlos y sale con el servidor de la empresa o el de la plataforma.
/// </remarks>
/// <param name="SendMail"><c>sendmail</c>: enviar el correo al comprador.</param>
/// <param name="SendMailToMe"><c>sendmailtome</c>: enviar una copia al correo de la empresa emisora.</param>
/// <param name="CcList"><c>email_cc_list</c>: más direcciones que reciben copia.</param>
/// <param name="PosCustomerEmail">
/// <c>email_pos_customer</c>: dirección del comprador en una venta de punto de venta; reemplaza la del documento y es la
/// única forma de enviar el correo al consumidor final.
/// </param>
/// <param name="AttachedDocumentNamePrefix"><c>atacheddocument_name_prefix</c>: texto que va antes del nombre del adjunto.</param>
/// <param name="HtmlHeader"><c>html_header</c>: HTML propio que reemplaza el saludo.</param>
/// <param name="HtmlBody"><c>html_body</c>: HTML propio que reemplaza la tabla del documento.</param>
/// <param name="HtmlButtons"><c>html_buttons</c>: HTML propio que reemplaza los botones.</param>
/// <param name="HtmlFooter"><c>html_footer</c>: HTML propio para el pie.</param>
internal sealed record DocumentEmailRequest(
    bool SendMail,
    bool SendMailToMe,
    IReadOnlyList<string> CcList,
    string? PosCustomerEmail,
    string? AttachedDocumentNamePrefix,
    string? HtmlHeader,
    string? HtmlBody,
    string? HtmlButtons,
    string? HtmlFooter)
{
    /// <summary>Una solicitud que no pide correo.</summary>
    public static readonly DocumentEmailRequest None = new(false, false, [], null, null, null, null, null, null);

    /// <summary>
    /// Lee la solicitud guardada. Un valor cuenta como verdadero con las reglas de PHP, como lo leía ApiDian
    /// (<c>if($request->sendmail)</c>): falso, nulo, 0, <c>""</c>, <c>"0"</c> y una lista vacía son falsos; todo lo
    /// demás, incluido el texto <c>"false"</c>, es verdadero.
    /// </summary>
    /// <returns><see cref="None"/> si no hay solicitud o no es un objeto JSON.</returns>
    public static DocumentEmailRequest Parse(byte[]? requestJson)
    {
        if (requestJson is not { Length: > 0 })
        {
            return None;
        }

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(requestJson) as JsonObject;
        }
        catch (JsonException)
        {
            return None;
        }

        if (root is null)
        {
            return None;
        }

        return new DocumentEmailRequest(
            IsPhpTrue(root["sendmail"]),
            IsPhpTrue(root["sendmailtome"]),
            CcAddresses(root["email_cc_list"]),
            Text(root["email_pos_customer"]),
            Text(root["atacheddocument_name_prefix"]),
            Text(root["html_header"]),
            Text(root["html_body"]),
            Text(root["html_buttons"]),
            Text(root["html_footer"]));
    }

    private static bool IsPhpTrue(JsonNode? node) => node switch
    {
        null => false,
        JsonArray array => array.Count > 0,
        JsonObject => true,
        JsonValue value => value.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => value.GetValue<decimal>() != 0m,
            JsonValueKind.String => value.GetValue<string>() is { Length: > 0 } text && text != "0",
            _ => false,
        },
        _ => false,
    };

    /// <summary>Texto no vacío de un campo (también un número escrito sin comillas); nulo si falta o está vacío.</summary>
    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() switch
        {
            JsonValueKind.String => value.GetValue<string>(),
            JsonValueKind.Number => value.GetValue<decimal>().ToString(CultureInfo.InvariantCulture),
            _ => null,
        } is { Length: > 0 } text
            ? text
            : null;

    /// <summary>
    /// Direcciones de <c>email_cc_list</c>. El PHP la valida como lista de objetos con <c>email</c>
    /// (<c>email_cc_list.*.email</c>); también se aceptan textos sueltos, que Laravel envía igual.
    /// </summary>
    private static List<string> CcAddresses(JsonNode? node) =>
        node is JsonArray list
            ? list.Select(item => item is JsonObject entry ? Text(entry["email"]) : Text(item)).OfType<string>().ToList()
            : [];
}
