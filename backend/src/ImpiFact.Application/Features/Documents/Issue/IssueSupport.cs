using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Documents.Issue;

/// <summary>
/// Limpieza de textos como la hacen las plantillas XML del PHP: cada valor pasa por
/// <c>preg_replace("/[\r\n|\n|\r]+/", "", …)</c>, que quita los retornos de carro (CR), los saltos de línea (LF) y el
/// carácter <c>|</c> (la expresión es una clase de caracteres). Sin esto, un salto de línea en una descripción
/// cambiaría el XML y el CUFE (código único de la factura electrónica) frente al PHP.
/// </summary>
internal static partial class PhpText
{
    public static string Clean(string text) => Stripped().Replace(text, "");

    public static string? CleanOrNull(LegacyScalar? value) => value is { } scalar ? Clean(scalar.Text) : null;

    [GeneratedRegex("[\r\n|]+", RegexOptions.CultureInvariant)]
    private static partial Regex Stripped();
}

/// <summary>Hora de Colombia (UTC-5 todo el año, sin horario de verano), en la que van la fecha y la hora del XML.</summary>
internal static class ColombiaTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(-5);

    public static DateTimeOffset Local(DateTimeOffset instant) => instant.ToOffset(Offset);

    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(Local(now).DateTime);

    /// <summary>Hora actual sin fracciones de segundo, como <c>date('H:i:s')</c>.</summary>
    public static TimeOnly Now(DateTimeOffset now)
    {
        var local = Local(now);
        return new TimeOnly(local.Hour, local.Minute, local.Second);
    }

    /// <summary>El instante UTC de una fecha y hora de Colombia.</summary>
    public static DateTimeOffset ToUtc(DateOnly date, TimeOnly time) => new DateTimeOffset(date.ToDateTime(time), Offset).ToUniversalTime();

    /// <summary>
    /// Días entre hoy y <paramref name="until"/> en Colombia, como <c>days_between_dates</c> y
    /// <c>certificate_days_left</c> del PHP.
    /// </summary>
    public static int DaysLeft(DateOnly until, DateTimeOffset now) => until.DayNumber - Today(now).DayNumber;

    public static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>Errores esperados de la emisión de documentos.</summary>
public static class IssueErrors
{
    public static readonly Error CompanyInactive = new(
        "company.inactive",
        "La empresa se encuentra en el momento INACTIVA para enviar documentos electronicos...",
        ErrorType.Forbidden);

    public static readonly Error CertificateMissing = new(
        "certificate.missing",
        "La empresa no tiene un certificado digital activo para firmar documentos.",
        ErrorType.Validation);

    /// <summary>Mismo texto del PHP (<c>verify_certificate</c>).</summary>
    public static readonly Error CertificateExpired = new(
        "certificate.expired",
        "El certificado digital ya se encuentra vencido...",
        ErrorType.Validation);

    public static readonly Error SoftwareMissing = new(
        "software.missing",
        "La empresa no tiene configurado el software de facturación para el ambiente actual.",
        ErrorType.Validation);

    /// <summary>The support document and its adjustment note need the support-document software (php-rules 13.1, item 6).</summary>
    public static readonly Error SupportDocumentSoftwareMissing = new(
        "software.missing",
        "La empresa no tiene configurado el software de documento soporte para el ambiente actual de documento soporte. " +
        "Regístrelo con idsupportdocument y pinsupportdocument.",
        ErrorType.Validation);

    public const string SupportDocumentReferenceNotFoundMessage =
        "El documento soporte de billing_reference no existe en la empresa. Envíe el CUDS de un documento soporte emitido por la empresa.";

    /// <summary>
    /// The adjustment note references, by its CUDS, something that is not a support document of the company (php-rules 14,
    /// item 2). The same answer for a CUDS of another company, so it never reveals other companies' documents.
    /// </summary>
    public static readonly Error SupportDocumentReferenceNotFound = new(
        "document.reference_not_found", SupportDocumentReferenceNotFoundMessage, ErrorType.Validation)
    {
        Fields = [new FieldError("billing_reference.uuid", SupportDocumentReferenceNotFoundMessage)],
    };

    public const string SupportDocumentReferenceNumberMismatchMessage =
        "billing_reference.number no corresponde al número del documento soporte de ese CUDS.";

    public static readonly Error SupportDocumentReferenceNumberMismatch = new(
        "document.reference_mismatch", SupportDocumentReferenceNumberMismatchMessage, ErrorType.Validation)
    {
        Fields = [new FieldError("billing_reference.number", SupportDocumentReferenceNumberMismatchMessage)],
    };

    public const string SupportDocumentReferenceNotAcceptedMessage =
        "El documento soporte referenciado en billing_reference todavía no está aceptado por la DIAN. Emita la nota de ajuste " +
        "cuando el documento soporte quede aceptado.";

    public static readonly Error SupportDocumentReferenceNotAccepted = new(
        "document.reference_not_accepted", SupportDocumentReferenceNotAcceptedMessage, ErrorType.Validation)
    {
        Fields = [new FieldError("billing_reference.uuid", SupportDocumentReferenceNotAcceptedMessage)],
    };

    public static readonly Error InvalidJson = Error.Validation(
        [new FieldError("body", "La solicitud no es un JSON válido con la forma del contrato.")]);

    public static readonly Error IdempotencyKeyReused = new(
        "idempotency.key_reused",
        "La llave Idempotency-Key ya se usó con una solicitud distinta.",
        ErrorType.Conflict);

    public static readonly Error IdempotencyKeyTooLong = Error.Validation(
        [new FieldError("Idempotency-Key", "Idempotency-Key no debe ser mayor que 100 caracteres.")]);

    /// <summary>El número ya lo usa otro documento con una solicitud distinta (supuesto B2).</summary>
    public static Error NumberInUse(string fullNumber) => new(
        "document.number_in_use",
        $"El número {fullNumber} ya lo usa otro documento emitido con una solicitud distinta. Envíe otro número o no envíe number para que se asigne el siguiente.",
        ErrorType.Conflict);

    /// <summary>
    /// El consecutivo de la resolución choca una y otra vez con números ya usados (por ejemplo, documentos cargados con
    /// números por encima del consecutivo, u otra resolución con el mismo prefijo).
    /// </summary>
    public static Error SequenceBlocked(string prefix) => new(
        "numbering.sequence_blocked",
        $"El consecutivo de la resolución con prefijo «{prefix}» llegó a números que ya usan otros documentos. Envíe el número en el campo number o revise la resolución.",
        ErrorType.Conflict);

    public static readonly Error SequenceMissing = new(
        "numbering.sequence_missing",
        "La resolución no tiene consecutivo de numeración. Vuelva a guardar la resolución para crearlo.",
        ErrorType.Validation);

    /// <summary>
    /// Texto de <see cref="ReferenceNotAccepted"/>. El PHP no buscaba la factura referenciada («php-rules», sección 4.2),
    /// así que no tiene un mensaje para esto.
    /// </summary>
    public const string ReferenceNotAcceptedMessage =
        "La factura referenciada en billing_reference todavía no está aceptada por la DIAN. Emita la nota cuando la factura quede aceptada.";

    /// <summary>
    /// La nota referencia por su CUFE (código único de la factura electrónica) una factura de la empresa que la DIAN
    /// todavía no aceptó (o que rechazó): una nota solo corrige un documento ya válido.
    /// </summary>
    public static readonly Error ReferenceNotAccepted = new(
        "document.reference_not_accepted", ReferenceNotAcceptedMessage, ErrorType.Validation)
    {
        Fields = [new FieldError("billing_reference.uuid", ReferenceNotAcceptedMessage)],
    };

    public static readonly Error RangeExhausted = Error.Validation(
        [new FieldError("number", "Se agotó el rango de numeración de la resolución. Configure una resolución nueva.")]);
}

/// <summary>
/// Respuesta de la emisión: los campos heredados del PHP que tienen sentido en la fase 3 (sin envío a la DIAN, así que
/// <c>ResponseDian</c> es nulo) más el documento canónico de ImpiFact. La factura lleva <c>cufe</c> y la nota
/// <c>cude</c>; las notas no llevan <c>success</c> ni <c>resolution_days_left</c>, igual que el PHP.
/// </summary>
public sealed record IssueDocumentResponse
{
    [JsonPropertyName("success")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Success { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>
    /// Nombre del XML firmado como lo da el PHP (<c>FES-</c>, <c>NCS-</c> o <c>NDS-</c> más el número completo), el que
    /// entiende la ruta <c>download</c>.
    /// </summary>
    [JsonPropertyName("urlinvoicexml")]
    public required string UrlInvoiceXml { get; init; }

    /// <summary>Nombre del PDF como lo da el PHP; se puede descargar cuando el Worker lo genera.</summary>
    [JsonPropertyName("urlinvoicepdf")]
    public required string UrlInvoicePdf { get; init; }

    /// <summary>
    /// Nombre del AttachedDocument (contenedor que se entrega al comprador) con el prefijo <c>Attachment-</c> que
    /// entiende la ruta <c>download</c>; existe cuando la DIAN acepta el documento.
    /// </summary>
    [JsonPropertyName("urlinvoiceattached")]
    public required string UrlInvoiceAttached { get; init; }

    [JsonPropertyName("cufe")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cufe { get; init; }

    [JsonPropertyName("cude")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cude { get; init; }

    /// <summary>
    /// CUDS (unique code of the support document) of a support document or its adjustment note, as the PHP names it
    /// (php-rules 13.3, item 8); those documents carry neither <c>cufe</c> nor <c>cude</c>.
    /// </summary>
    [JsonPropertyName("cuds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cuds { get; init; }

    [JsonPropertyName("QRStr")]
    public required string QrStr { get; init; }

    [JsonPropertyName("certificate_days_left")]
    public int CertificateDaysLeft { get; init; }

    [JsonPropertyName("resolution_days_left")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ResolutionDaysLeft { get; init; }

    /// <summary>Respuesta de la DIAN; siempre nula en la fase 3 porque el envío lo hará el Worker.</summary>
    [JsonPropertyName("ResponseDian")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public object? ResponseDian { get; init; }

    /// <summary>XML firmado en base64.</summary>
    [JsonPropertyName("invoicexml")]
    public string? InvoiceXml { get; init; }

    /// <summary>XML sin firmar en base64; nulo cuando la respuesta repite un documento ya emitido.</summary>
    [JsonPropertyName("unsignedinvoicexml")]
    public string? UnsignedInvoiceXml { get; init; }

    /// <summary>ZIP para la DIAN; nulo en la fase 3 (lo arma el envío).</summary>
    [JsonPropertyName("zipinvoicexml")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ZipInvoiceXml { get; init; }

    [JsonPropertyName("document")]
    public required IssuedDocumentInfo Document { get; init; }
}

/// <summary>El documento emitido (campos canónicos de ImpiFact).</summary>
public sealed record IssuedDocumentInfo
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("document_type_id")]
    public int DocumentTypeId { get; init; }

    [JsonPropertyName("type_environment_id")]
    public int TypeEnvironmentId { get; init; }

    [JsonPropertyName("prefix")]
    public string Prefix { get; init; } = "";

    [JsonPropertyName("number")]
    public long Number { get; init; }

    [JsonPropertyName("full_number")]
    public string FullNumber { get; init; } = "";

    [JsonPropertyName("status")]
    public string Status { get; init; } = "";

    [JsonPropertyName("uuid")]
    public string? Uuid { get; init; }

    [JsonPropertyName("uuid_algorithm")]
    public string? UuidAlgorithm { get; init; }

    /// <summary>Verdadero si la solicitud repitió una ya atendida y se respondió con el documento existente.</summary>
    [JsonPropertyName("replayed")]
    public bool Replayed { get; init; }

    /// <summary>Batch the document was added to (phase 7a, T8); absent for a document sent on its own.</summary>
    [JsonPropertyName("batch_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? BatchId { get; init; }
}
