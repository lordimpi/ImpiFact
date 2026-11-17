using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Dian.Soap;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Features.Documents.Queries.DianStatus;

/// <summary>
/// <c>POST status/document/{trackId}</c> del PHP: estado en la DIAN (la autoridad tributaria de Colombia) de un
/// documento por su CUFE o CUDE (códigos únicos de factura y de documento electrónico).
/// </summary>
/// <remarks>
/// El PHP consultaba GetStatus en la DIAN en cada llamada. Aquí la API no llama a la DIAN: responde con la última
/// respuesta que guardó el Worker al enviar el documento o al consultar su estado (Arquitectura detallada, sección 7;
/// decisión L16 de la fase 4).
/// </remarks>
public sealed record GetDocumentDianStatusQuery(string TrackId);

/// <summary>
/// <c>POST status/zip/{trackId}</c> del PHP: estado de un envío del set de pruebas de habilitación por su ZipKey (la
/// clave que devolvió SendTestSetAsync), con la última respuesta de GetStatusZip que guardó el Worker.
/// </summary>
public sealed record GetZipDianStatusQuery(string TrackId);

/// <summary>
/// <c>POST xml/document/{trackId}</c> del PHP: el XML firmado del documento, con la forma de la respuesta de
/// GetXmlByDocumentKey de la DIAN. El PHP lo pedía a la DIAN; aquí sale del XML firmado guardado al emitir, que es el
/// mismo que la DIAN validó, y solo para documentos aceptados.
/// </summary>
public sealed record GetDocumentXmlQuery(string TrackId);

/// <summary>
/// Respuesta de las consultas de estado: los campos del PHP (<c>message</c>, <c>ResponseDian</c> con el sobre de la
/// DIAN convertido como lo hacía el PHP, <c>reqzip</c> y <c>rptazip</c>) más el documento de ImpiFact.
/// </summary>
public sealed record DianStatusResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; } = true;

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";

    /// <summary>Sobre de la última respuesta de la DIAN; nulo si todavía no hay respuesta.</summary>
    [JsonPropertyName("ResponseDian")]
    public JsonObject? ResponseDian { get; init; }

    /// <summary>Sobre de la consulta en Base64 en el PHP. ImpiFact no guarda los sobres enviados: siempre nulo.</summary>
    [JsonPropertyName("reqzip")]
    public string? RequestBase64 { get; init; }

    /// <summary>Respuesta de la DIAN tal como llegó, en Base64; nula si todavía no hay respuesta.</summary>
    [JsonPropertyName("rptazip")]
    public string? ResponseBase64 { get; init; }

    /// <summary>
    /// AttachedDocument (contenedor que se entrega al comprador) en Base64, como el PHP; nulo si todavía no existe
    /// (se genera cuando la DIAN acepta el documento).
    /// </summary>
    [JsonPropertyName("attacheddocument")]
    public string? AttachedDocument { get; init; }

    /// <summary>Días que le quedan al certificado de la empresa (<c>certificate_days_left</c> del PHP).</summary>
    [JsonPropertyName("certificate_days_left")]
    public int CertificateDaysLeft { get; init; }

    [JsonPropertyName("document")]
    public UtilityDocumentResponse Document { get; init; } = null!;
}

/// <summary>Respuesta de <c>xml/document</c>, con la forma del PHP.</summary>
public sealed record DocumentXmlResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; } = true;

    [JsonPropertyName("message")]
    public string Message { get; init; } = DianStatusMessages.Answered;

    /// <summary>
    /// <c>Envelope.Body.GetXmlByDocumentKeyResponse.GetXmlByDocumentKeyResult</c> con <c>Code</c> 100,
    /// <c>Message</c> y <c>XmlBytesBase64</c> (el XML firmado en Base64), como la respuesta de la DIAN.
    /// </summary>
    [JsonPropertyName("ResponseDian")]
    public JsonObject ResponseDian { get; init; } = [];

    [JsonPropertyName("certificate_days_left")]
    public int CertificateDaysLeft { get; init; }
}

/// <summary>El documento de ImpiFact al que se refiere una respuesta utilitaria.</summary>
/// <param name="StateDocumentId">Estado heredado del PHP (ver <see cref="LegacyDocumentState"/>).</param>
public sealed record UtilityDocumentResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("document_type_id")] int DocumentTypeId,
    [property: JsonPropertyName("full_number")] string FullNumber,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("state_document_id")] int StateDocumentId,
    [property: JsonPropertyName("cufe")] string? Cufe)
{
    public static UtilityDocumentResponse From(DocumentRef document) => new(
        document.Id, document.DocumentTypeId, document.FullNumber, document.Status.ToString(), LegacyDocumentState.Of(document.Status),
        document.Cufe);
}

/// <summary>Textos de las consultas de estado.</summary>
public static class DianStatusMessages
{
    /// <summary>Texto del PHP cuando la DIAN respondió.</summary>
    public const string Answered = "Consulta generada con éxito";

    public const string NotAnsweredYet = "La DIAN todavía no ha respondido sobre el documento; consulte de nuevo más tarde.";
}

internal sealed class GetDocumentDianStatusHandler(
    IDocumentLookups lookups, IDocumentFileReads files, IIssuerContextReads issuers, ITenantContext tenant, IClock clock)
    : IHandler<GetDocumentDianStatusQuery, DianStatusResponse>
{
    public async Task<Result<DianStatusResponse>> HandleAsync(GetDocumentDianStatusQuery request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId || await lookups.FindByCufeAsync(companyId, request.TrackId.Trim(), ct) is not { } document)
        {
            return DocumentErrors.NotFound;
        }

        var answer = DianStatusAnswer.Of(document, await lookups.LatestDianAnswerAsync(companyId, document.Id, ct));
        return await DianStatusAnswer.WithCompanyFieldsAsync(answer, companyId, document, files, issuers, clock, ct);
    }
}

internal sealed class GetZipDianStatusHandler(
    IDocumentLookups lookups, IDocumentFileReads files, IIssuerContextReads issuers, ITenantContext tenant, IClock clock)
    : IHandler<GetZipDianStatusQuery, DianStatusResponse>
{
    public async Task<Result<DianStatusResponse>> HandleAsync(GetZipDianStatusQuery request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId || await lookups.FindZipAnswerAsync(companyId, request.TrackId.Trim(), ct) is not { } answer)
        {
            return DocumentErrors.NotFound;
        }

        return await DianStatusAnswer.WithCompanyFieldsAsync(
            DianStatusAnswer.Of(answer.Document, answer.Body), companyId, answer.Document, files, issuers, clock, ct);
    }
}

internal sealed class GetDocumentXmlHandler(
    IDocumentLookups lookups, IDocumentFileReads files, IIssuerContextReads issuers, ITenantContext tenant, IClock clock)
    : IHandler<GetDocumentXmlQuery, DocumentXmlResponse>
{
    /// <summary>Código de GetXmlByDocumentKey cuando la DIAN encuentra el documento.</summary>
    private const string FoundCode = "100";

    public async Task<Result<DocumentXmlResponse>> HandleAsync(GetDocumentXmlQuery request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId || await lookups.FindByCufeAsync(companyId, request.TrackId.Trim(), ct) is not { } document)
        {
            return DocumentErrors.NotFound;
        }

        if (!LegacyDocumentState.Validated.Contains(document.Status))
        {
            return DocumentErrors.NotAccepted(document.FullNumber);
        }

        if (await files.ReadLatestAsync(companyId, document.Id, DocumentFileKind.SignedXml, ct) is not { } xml)
        {
            return DocumentErrors.FileNotFound;
        }

        var daysLeft = await DianStatusAnswer.CertificateDaysLeftAsync(issuers, clock, companyId, document.DocumentTypeId, ct);
        return new DocumentXmlResponse
        {
            ResponseDian = new JsonObject
            {
                ["Envelope"] = new JsonObject
                {
                    ["Body"] = new JsonObject
                    {
                        ["GetXmlByDocumentKeyResponse"] = new JsonObject
                        {
                            ["GetXmlByDocumentKeyResult"] = new JsonObject
                            {
                                ["Code"] = FoundCode,
                                ["Message"] = "Accion completada OK",
                                ["XmlBytesBase64"] = Convert.ToBase64String(xml),
                            },
                        },
                    },
                },
            },
            CertificateDaysLeft = daysLeft,
        };
    }
}

/// <summary>Arma la respuesta de estado a partir de la última respuesta guardada de la DIAN.</summary>
internal static class DianStatusAnswer
{
    public static DianStatusResponse Of(DocumentRef document, byte[]? body)
    {
        if (body is null)
        {
            return new DianStatusResponse { Message = DianStatusMessages.NotAnsweredYet, Document = UtilityDocumentResponse.From(document) };
        }

        return new DianStatusResponse
        {
            Message = DianStatusMessages.Answered,
            ResponseDian = DianLegacyJson.FromXml(Encoding.UTF8.GetString(body)),
            ResponseBase64 = Convert.ToBase64String(body),
            Document = UtilityDocumentResponse.From(document),
        };
    }

    /// <summary>Agrega lo que el PHP también respondía: el AttachedDocument y los días que le quedan al certificado.</summary>
    public static async Task<DianStatusResponse> WithCompanyFieldsAsync(
        DianStatusResponse response, Guid companyId, DocumentRef document, IDocumentFileReads files, IIssuerContextReads issuers, IClock clock,
        CancellationToken ct)
    {
        var attached = await files.ReadLatestAsync(companyId, document.Id, DocumentFileKind.AttachedDocument, ct);
        return response with
        {
            AttachedDocument = attached is null ? null : Convert.ToBase64String(attached),
            CertificateDaysLeft = await CertificateDaysLeftAsync(issuers, clock, companyId, document.DocumentTypeId, ct),
        };
    }

    /// <summary>Días que le quedan al certificado activo de la empresa, contados como el PHP; 0 si no tiene.</summary>
    public static async Task<int> CertificateDaysLeftAsync(
        IIssuerContextReads issuers, IClock clock, Guid companyId, int documentTypeId, CancellationToken ct)
    {
        var certificate = (await issuers.GetAsync(companyId, documentTypeId, DocumentKind.Invoice, ct))?.Certificate;
        return certificate is null ? 0 : ColombiaTime.DaysLeft(ColombiaTime.Today(certificate.NotAfter), clock.UtcNow);
    }
}

/// <summary>El <c>trackId</c> es obligatorio y cabe en la columna del CUFE (96) o de la ZipKey (64).</summary>
internal sealed class GetDocumentDianStatusQueryValidator : AbstractValidator<GetDocumentDianStatusQuery>
{
    public GetDocumentDianStatusQueryValidator() => RuleFor(query => query.TrackId).TrackId(96);
}

internal sealed class GetZipDianStatusQueryValidator : AbstractValidator<GetZipDianStatusQuery>
{
    public GetZipDianStatusQueryValidator() => RuleFor(query => query.TrackId).TrackId(64);
}

internal sealed class GetDocumentXmlQueryValidator : AbstractValidator<GetDocumentXmlQuery>
{
    public GetDocumentXmlQueryValidator() => RuleFor(query => query.TrackId).TrackId(96);
}

internal static class TrackIdRules
{
    public static IRuleBuilderOptions<T, string> TrackId<T>(this IRuleBuilderInitial<T, string> rule, int maxLength) => rule
        .Cascade(CascadeMode.Stop)
        .Must(value => !string.IsNullOrWhiteSpace(value)).WithMessage(QueryMessages.Required("trackId"))
        .MaximumLength(maxLength).WithMessage(QueryMessages.MaxLength("trackId", maxLength))
        .OverridePropertyName("trackId");
}
