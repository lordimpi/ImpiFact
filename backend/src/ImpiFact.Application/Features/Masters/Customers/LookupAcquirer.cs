using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Dian;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Dian.Soap;
using ImpiFact.Domain.Common;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Features.Masters.Customers;

/// <summary>
/// <c>GET /api/v1/customers/lookup</c>: pregunta en vivo a la DIAN (la autoridad tributaria de Colombia, servicio
/// GetAcquirer) qué nombre y qué correo de recepción tiene registrados un adquiriente (comprador), para llenar el
/// formulario del cliente. Equivale a <c>GET customer/{document}/{nit}</c> del PHP (<c>CustomerController::getAcquirer</c>).
/// </summary>
/// <param name="Type">Tipo de identificación (id del catálogo <c>type_document_identifications</c>; 6 es NIT).</param>
/// <param name="Number">Número de identificación sin dígito de verificación.</param>
public sealed record LookupAcquirerQuery(int? Type, string? Number);

internal sealed class LookupAcquirerQueryValidator : AbstractValidator<LookupAcquirerQuery>
{
    public LookupAcquirerQueryValidator()
    {
        RuleFor(query => query.Type)
            .NotNull().WithMessage(MasterMessages.Required("type"))
            .OverridePropertyName("type");
        RuleFor(query => MasterText.Clean(query.Number))
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(MasterMessages.Required("number"))
            .Must(number => number!.All(char.IsAsciiLetterOrDigit)).WithMessage(MasterMessages.AlphaNum("number"))
            .MaximumLength(15).WithMessage(MasterMessages.BetweenLength("number", 1, 15))
            .OverridePropertyName("number");
    }
}

/// <summary>
/// Firma el sobre con el certificado activo de la empresa y llama a la DIAN del ambiente actual de facturación, como la
/// consulta de rangos de numeración (y como el PHP, que usa el certificado de la empresa). A la DIAN va el código del
/// tipo de identificación (31 para NIT), no el id del catálogo. Respuestas: 200 con <c>found</c> verdadero (código 200 de
/// la DIAN) o falso (404); otro código de la DIAN o un SOAP Fault (la DIAN rechazó el mensaje) responden 422, y la falta
/// de respuesta o un servicio caído, 503. Los registros nunca llevan el certificado, el sobre ni la identificación
/// consultada (es un dato personal).
/// </summary>
internal sealed class LookupAcquirerHandler(
    IIssuerContextReads issuers,
    IDocumentCatalogs catalogs,
    ICertificateStore certificates,
    IDianServiceUrls urls,
    IDianWebService dian,
    ITenantContext tenant,
    IClock clock,
    ILogger<LookupAcquirerHandler> logger) : IHandler<LookupAcquirerQuery, AcquirerLookupResponse>
{
    /// <summary>Factura electrónica: el ambiente y el certificado de la consulta son los de la facturación.</summary>
    private const int InvoiceDocumentType = 1;

    private const string FoundCode = "200";

    private const string NotFoundCode = "404";

    public async Task<Result<AcquirerLookupResponse>> HandleAsync(LookupAcquirerQuery request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId)
        {
            return MasterErrors.CertificateNotFound;
        }

        var key = new CatalogKey(DianCatalog.IdentificationType, request.Type!.Value);
        if (!(await catalogs.FindAsync([key], ct)).TryGetValue(key, out var identificationType))
        {
            return Error.Validation([new FieldError("type", MasterMessages.Invalid("type"))]);
        }

        var issuer = await issuers.GetAsync(companyId, InvoiceDocumentType, DocumentKind.Invoice, ct);
        if (issuer?.Certificate is not { } active)
        {
            return MasterErrors.CertificateNotFound;
        }

        if (active.NotAfter <= clock.UtcNow)
        {
            return MasterErrors.CertificateExpired;
        }

        var operation = new GetAcquirerRequest(identificationType.Code.Trim(), MasterText.Clean(request.Number)!);
        DianCall call;
        using (var certificate = await certificates.LoadAsync(companyId, active.SecretRef, ct))
        {
            call = await dian.CallAsync(operation, urls.ForEnvironment(issuer.TypeEnvironmentId), certificate, ct);
        }

        logger.LogInformation(
            "DIAN GetAcquirer for company {CompanyId} ended as {Outcome} with HTTP {HttpStatus} in {DurationMs} ms.",
            companyId, call.Outcome, call.HttpStatus, (int)call.Duration.TotalMilliseconds);

        return call switch
        {
            { Outcome: DianCallOutcome.Answered, Response: DianAcquirerResponse { StatusCode: FoundCode } found } => new AcquirerLookupResponse
            {
                Found = true,
                StatusCode = found.StatusCode,
                Message = found.Message,
                Name = found.ReceiverName,
                Email = found.ReceiverEmail,
            },
            { Outcome: DianCallOutcome.Answered, Response: DianAcquirerResponse { StatusCode: NotFoundCode } missing } => new AcquirerLookupResponse
            {
                Found = false,
                StatusCode = missing.StatusCode,
                Message = missing.Message,
            },
            { Outcome: DianCallOutcome.Answered, Response: DianAcquirerResponse other } => Rejected(other),
            { Outcome: DianCallOutcome.Answered, Response: DianSoapFaultResponse fault } => MasterErrors.DianFault(fault.Reason),
            _ => Unavailable(call),
        };
    }

    private Error Rejected(DianAcquirerResponse answer)
    {
        logger.LogWarning("DIAN GetAcquirer answered status {StatusCode}.", answer.StatusCode ?? "none");
        return MasterErrors.DianRejected(answer.Message);
    }

    private Error Unavailable(DianCall call)
    {
        logger.LogWarning(
            "DIAN GetAcquirer got no usable answer ({Outcome}, {Answer}): {Failure}",
            call.Outcome, call.Response?.GetType().Name ?? "none", call.Failure ?? "the answer was not recognized");
        return MasterErrors.DianUnavailable;
    }
}
