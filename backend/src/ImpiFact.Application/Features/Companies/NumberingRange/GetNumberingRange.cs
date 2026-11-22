using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Dian;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Dian.Soap;
using ImpiFact.Domain.Common;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Features.Companies.NumberingRange;

/// <summary>
/// Rangos de numeración que la DIAN (la autoridad tributaria de Colombia) tiene autorizados para la empresa de la
/// credencial y un software (<c>POST numbering-range</c> del PHP, carpeta 06 «Paso a producción» del Postman). Es la
/// única consulta que la API hace en vivo a la DIAN: llama a GetNumberingRange con el NIT (número de identificación
/// tributaria) de la empresa como emisor y como proveedor tecnológico, igual que el PHP (software propio).
/// </summary>
/// <param name="Nit">NIT de la ruta, que debe ser el de la empresa de la credencial.</param>
/// <param name="SoftwareId">Identificador del software ante la DIAN (<c>IDSoftware</c> del PHP).</param>
/// <param name="SaveIn">
/// <c>GuardarEn</c> del PHP: carpeta del servidor donde guardar la solicitud y la respuesta. Se acepta por el contrato y
/// se ignora: la API nunca escribe en rutas que manda el cliente.
/// </param>
public sealed record GetNumberingRangeQuery(
    [property: JsonPropertyName("IDSoftware")] string? SoftwareId,
    [property: JsonPropertyName("GuardarEn")] string? SaveIn = null)
{
    [JsonIgnore]
    public string? Nit { get; init; }
}

/// <summary>
/// Respuesta del PHP (<c>message</c>, <c>ResponseDian</c> con el sobre completo y <c>certificate_days_left</c>) más
/// lo que ImpiFact lee del sobre: si la DIAN completó la consulta, su código y los rangos.
/// </summary>
public sealed record NumberingRangeResponse
{
    /// <summary>Verdadero si la DIAN completó la consulta (código de operación 100).</summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = GetNumberingRangeHandler.SuccessMessage;

    [JsonPropertyName("ResponseDian")]
    public JsonObject? ResponseDian { get; init; }

    [JsonPropertyName("certificate_days_left")]
    public int CertificateDaysLeft { get; init; }

    [JsonPropertyName("operation_code")]
    public string? OperationCode { get; init; }

    [JsonPropertyName("operation_description")]
    public string? OperationDescription { get; init; }

    [JsonPropertyName("numbering_ranges")]
    public IReadOnlyList<NumberingRangeItem> NumberingRanges { get; init; } = [];
}

/// <summary>Un rango autorizado, con fechas <c>yyyy-MM-dd</c>.</summary>
public sealed record NumberingRangeItem(
    [property: JsonPropertyName("resolution_number")] string? ResolutionNumber,
    [property: JsonPropertyName("resolution_date")] DateOnly? ResolutionDate,
    [property: JsonPropertyName("prefix")] string? Prefix,
    [property: JsonPropertyName("from")] long? From,
    [property: JsonPropertyName("to")] long? To,
    [property: JsonPropertyName("valid_date_from")] DateOnly? ValidDateFrom,
    [property: JsonPropertyName("valid_date_to")] DateOnly? ValidDateTo,
    [property: JsonPropertyName("technical_key")] string? TechnicalKey);

/// <summary>Regla de <c>NumberingRangeRequest.php</c>: <c>IDSoftware</c> obligatorio; el largo sale de la columna del software.</summary>
internal sealed class GetNumberingRangeQueryValidator : AbstractValidator<GetNumberingRangeQuery>
{
    private const int MaxLength = 64;

    public GetNumberingRangeQueryValidator()
    {
        RuleFor(query => query.SoftwareId)
            .Cascade(CascadeMode.Stop)
            .Must(id => !string.IsNullOrWhiteSpace(id)).WithMessage(PhpMessages.Required("IDSoftware"))
            .MaximumLength(MaxLength).WithMessage(PhpMessages.MaxLength("IDSoftware", MaxLength))
            .OverridePropertyName("IDSoftware");
    }
}

/// <summary>
/// Firma el sobre con el certificado activo de la empresa y llama a la DIAN del ambiente actual de facturación (por
/// defecto habilitación, como una empresa nueva). Como el PHP, exige un certificado vigente. La respuesta de la DIAN,
/// sea cual sea su código de operación, se devuelve con 200; un SOAP Fault (la DIAN rechazó el mensaje) responde 422 y
/// la falta de respuesta o un servicio caído, 503. Los registros nunca llevan el certificado ni el sobre.
/// </summary>
internal sealed class GetNumberingRangeHandler(
    ICompanyRepository companies,
    ICertificateStore certificates,
    IDianServiceUrls urls,
    IDianWebService dian,
    ITenantContext tenant,
    IClock clock,
    ILogger<GetNumberingRangeHandler> logger) : IHandler<GetNumberingRangeQuery, NumberingRangeResponse>
{
    /// <summary>Texto del PHP cuando la DIAN responde.</summary>
    public const string SuccessMessage = "Consulta generada con éxito";

    /// <summary>Código de operación de una consulta completada.</summary>
    private const string CompletedCode = "100";

    /// <summary>Habilitación (pruebas): ambiente de una empresa que todavía no eligió el suyo.</summary>
    private const int DefaultEnvironment = 2;

    public async Task<Result<NumberingRangeResponse>> HandleAsync(GetNumberingRangeQuery request, CancellationToken ct)
    {
        var own = await CompanyAccess.OwnCompanyAsync(companies, tenant, request.Nit, ct);
        if (!own.IsSuccess)
        {
            return own.Error;
        }

        var company = own.Value;
        if (await companies.GetActiveCertificateAsync(company.Id, ct) is not { } active)
        {
            return CompanyErrors.CertificateNotFound;
        }

        var daysLeft = CertificateDays.Left(active.NotAfter, clock.UtcNow);
        if (daysLeft <= 0)
        {
            return CompanyErrors.CertificateExpired;
        }

        var environment = (await companies.GetEnvironmentsAsync(company.Id, ct))
            .FirstOrDefault(row => row.DocumentKind == DocumentKind.Invoice)?.TypeEnvironmentId ?? DefaultEnvironment;
        var operation = new GetNumberingRangeRequest(company.IdentificationNumber, company.IdentificationNumber, request.SoftwareId!.Trim());

        DianCall call;
        using (var certificate = await certificates.LoadAsync(company.Id, active.SecretRef, ct))
        {
            call = await dian.CallAsync(operation, urls.ForEnvironment(environment), certificate, ct);
        }

        logger.LogInformation(
            "DIAN GetNumberingRange for company {CompanyId} ended as {Outcome} with HTTP {HttpStatus} in {DurationMs} ms.",
            company.Id, call.Outcome, call.HttpStatus, (int)call.Duration.TotalMilliseconds);

        return call switch
        {
            { Outcome: DianCallOutcome.Answered, Response: DianNumberingRangeResponse ranges } => new NumberingRangeResponse
            {
                Success = ranges.OperationCode == CompletedCode,
                ResponseDian = DianLegacyJson.FromXml(call.Body!),
                CertificateDaysLeft = daysLeft,
                OperationCode = ranges.OperationCode,
                OperationDescription = ranges.OperationDescription,
                NumberingRanges = ranges.Ranges
                    .Select(range => new NumberingRangeItem(
                        range.ResolutionNumber, range.ResolutionDate, range.Prefix, range.FromNumber, range.ToNumber, range.ValidDateFrom,
                        range.ValidDateTo, range.TechnicalKey))
                    .ToList(),
            },
            { Outcome: DianCallOutcome.Answered, Response: DianSoapFaultResponse fault } => CompanyErrors.DianFault(fault.Reason),
            _ => Unavailable(call),
        };
    }

    private Error Unavailable(DianCall call)
    {
        logger.LogWarning(
            "DIAN GetNumberingRange got no usable answer ({Outcome}, {Answer}): {Failure}",
            call.Outcome, call.Response?.GetType().Name ?? "none", call.Failure ?? "the answer was not recognized");
        return CompanyErrors.DianUnavailable;
    }
}
