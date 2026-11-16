using ImpiFact.Dian.Cufe;
using ImpiFact.Dian.Ubl;

namespace ImpiFact.Dian.Attached;

/// <summary>Tipo del documento que va dentro del AttachedDocument (contenedor electrónico).</summary>
public enum AttachedDocumentKind
{
    /// <summary>Factura electrónica de venta: se identifica con su CUFE.</summary>
    Invoice,

    /// <summary>Nota crédito: se identifica con su CUDE.</summary>
    CreditNote,

    /// <summary>Nota débito: se identifica con su CUDE.</summary>
    DebitNote,
}

/// <summary>
/// Datos para armar el AttachedDocument (contenedor electrónico, tipo de documento 89 de la DIAN): el XML
/// UBL 2.1 (el estándar XML de documentos comerciales que usa la DIAN) que el emisor entrega al comprador
/// cuando la DIAN acepta una factura o una nota. Lleva el documento firmado y el ApplicationResponse (la
/// respuesta de validación de la DIAN, firmada por ella).
/// </summary>
public sealed record AttachedDocumentInput
{
    public required AttachedDocumentKind Kind { get; init; }

    /// <summary>Ambiente de la DIAN del emisor; se escribe en cbc:ProfileExecutionID.</summary>
    public required DianEnvironment Environment { get; init; }

    /// <summary>
    /// CUFE (código único de factura electrónica) o CUDE (código único de documento electrónico, de las
    /// notas) que devolvió la DIAN (XmlDocumentKey); es el cbc:ID del contenedor.
    /// </summary>
    public required string DocumentKey { get; init; }

    /// <summary>Número del documento: prefijo de la resolución y consecutivo, por ejemplo <c>SETP990000001</c>.</summary>
    public required string DocumentNumber { get; init; }

    /// <summary>
    /// Momento en que se genera el contenedor; se escribe en hora de Colombia en cbc:IssueDate y cbc:IssueTime.
    /// </summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>Emisor del documento (cac:SenderParty).</summary>
    public required InvoiceParty Sender { get; init; }

    /// <summary>Comprador que recibe el contenedor (cac:ReceiverParty).</summary>
    public required InvoiceParty Receiver { get; init; }

    /// <summary>XML firmado del documento, tal como se envió a la DIAN.</summary>
    public required string SignedDocumentXml { get; init; }

    /// <summary>
    /// Bytes del ApplicationResponse tal como llegaron en XmlBase64Bytes (ya decodificados del Base64), en UTF-8.
    /// </summary>
    public required byte[] ApplicationResponseXml { get; init; }
}
