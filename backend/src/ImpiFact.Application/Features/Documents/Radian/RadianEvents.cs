using System.Text;
using System.Text.Json.Serialization;
using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Billing;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Dian;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Application.Features.Documents.Queries;
using ImpiFact.Dian.Radian;
using ImpiFact.Domain.Documents;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Features.Documents.Radian;

/// <summary>The person who receives the invoice or the goods, or who decides (cac:IssuerParty/cac:Person).</summary>
public sealed record EventPersonBody
{
    [JsonPropertyName("identification_number")]
    public string? IdentificationNumber { get; init; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; init; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; init; }

    [JsonPropertyName("organization_department")]
    public string? OrganizationDepartment { get; init; }

    [JsonPropertyName("job_title")]
    public string? JobTitle { get; init; }
}

/// <summary>Body of <c>POST /api/v1/received-documents/{id}/events</c> and <c>POST /api/v1/documents/{id}/events</c>.</summary>
public sealed record SendEventBody
{
    /// <summary>DIAN code: 030, 031, 032 or 033 on a received invoice; 034 on an own invoice.</summary>
    [JsonPropertyName("event_code")]
    public string? EventCode { get; init; }

    /// <summary>Claim reason, only with the 031: 01 to 04 (<c>type_rejections</c>).</summary>
    [JsonPropertyName("claim_reason")]
    public string? ClaimReason { get; init; }

    /// <summary>Who receives or decides; without it the company is written, as the PHP does.</summary>
    [JsonPropertyName("issuer_party")]
    public EventPersonBody? IssuerParty { get; init; }
}

/// <summary><c>POST /api/v1/received-documents/{id}/events</c>: an event of the buyer on a received invoice (030 to 033).</summary>
public sealed record SendReceivedDocumentEventCommand(Guid Id, SendEventBody Body) : ICommand;

/// <summary><c>POST /api/v1/documents/{id}/events</c>: the tacit acceptance (034) of the seller on its own invoice.</summary>
public sealed record SendDocumentEventCommand(Guid Id, SendEventBody Body) : ICommand;

/// <summary><c>GET /api/v1/documents/{id}/events</c>: the events the company sent on its own invoice.</summary>
public sealed record ListDocumentEventsQuery(Guid Id);

/// <summary>
/// The events the DIAN has registered (GetStatusEvent) on an invoice of the company: an own one by <see cref="DocumentId"/>
/// or a received one by <see cref="ReceivedDocumentId"/>. A document that is not of the company answers 404 without
/// calling the DIAN.
/// </summary>
public sealed record GetDianEventsQuery(Guid? DocumentId, Guid? ReceivedDocumentId);

internal static class EventBodies
{
    public const int MaxNameLength = 100;

    public static EventPerson? Person(EventPersonBody? body) => body is null
        ? null
        : new EventPerson(
            body.IdentificationNumber!.Trim(), body.FirstName!.Trim(), body.LastName!.Trim(), body.JobTitle!.Trim(),
            body.OrganizationDepartment!.Trim());

    public static void PersonRules<T>(AbstractValidator<T> validator, Func<T, EventPersonBody?> person)
    {
        var fields = new (string Name, Func<EventPersonBody, string?> Value)[]
        {
            ("identification_number", p => p.IdentificationNumber),
            ("first_name", p => p.FirstName),
            ("last_name", p => p.LastName),
            ("organization_department", p => p.OrganizationDepartment),
            ("job_title", p => p.JobTitle),
        };
        foreach (var (name, value) in fields)
        {
            validator.RuleFor(request => person(request) == null ? null : value(person(request)!))
                .Cascade(CascadeMode.Stop)
                .Must(text => !string.IsNullOrWhiteSpace(text)).WithMessage(IssueMessages.RequiredWith(name, "issuer_party"))
                .Must(text => text!.Trim().Length <= MaxNameLength).WithMessage(IssueMessages.MaxLength(name, MaxNameLength))
                .When(request => person(request) is not null)
                .OverridePropertyName("issuer_party." + name);
        }
    }
}

internal static class EventBodyRules
{
    public static void Apply<T>(AbstractValidator<T> validator, Func<T, SendEventBody> body)
    {
        validator.RuleFor(request => body(request).EventCode)
            .Must(code => RadianCatalog.ByCode(code) is not null).WithMessage("El evento debe ser 030, 031, 032, 033 o 034.")
            .OverridePropertyName("event_code");
        validator.RuleFor(request => body(request).ClaimReason)
            .Must(reason => RadianCatalog.ClaimReason(reason) is { } found && found.Code == reason!.Trim())
            .When(request => body(request).EventCode?.Trim() == RadianCatalog.Claim)
            .WithMessage("El reclamo (031) requiere el motivo: 01, 02, 03 o 04.")
            .OverridePropertyName("claim_reason");
        validator.RuleFor(request => body(request).ClaimReason)
            .Must(string.IsNullOrWhiteSpace)
            .When(request => body(request).EventCode?.Trim() != RadianCatalog.Claim)
            .WithMessage("El motivo solo se informa con el reclamo (031).")
            .OverridePropertyName("claim_reason");
        EventBodies.PersonRules(validator, request => body(request).IssuerParty);
    }
}

internal sealed class SendReceivedDocumentEventCommandValidator : AbstractValidator<SendReceivedDocumentEventCommand>
{
    public SendReceivedDocumentEventCommandValidator() => EventBodyRules.Apply(this, command => command.Body);
}

internal sealed class SendDocumentEventCommandValidator : AbstractValidator<SendDocumentEventCommand>
{
    public SendDocumentEventCommandValidator() => EventBodyRules.Apply(this, command => command.Body);
}

/// <summary>The handlers of the events share the ports and the two targets: a received invoice and an own invoice.</summary>
internal abstract class RadianEventHandlerBase(RadianPorts ports, IDocumentReads reads)
{
    /// <summary>Invoice types an own 034 may reference: national, export, contingency and type 04.</summary>
    private static readonly int[] InvoiceTypes = [1, 2, 3, 12];

    private const string DianName = "Unidad Administrativa Especial Dirección de Impuestos y Aduanas Nacionales";

    protected RadianPorts Ports => ports;

    protected Guid CompanyId => ports.Tenant.CompanyId ?? throw new InvalidOperationException("The tenant decorator guarantees a company.");

    protected async Task<Result<RadianEventResponse>> SendOnReceivedAsync(
        ReceivedDocument document, EventRequest request, IssuerContext? knownIssuer, CancellationToken ct)
    {
        var issuer = knownIssuer;
        if (issuer is null)
        {
            var found = await RadianWorkflow.IssuerAsync(ports, CompanyId, ct);
            if (!found.IsSuccess)
            {
                return found.Error;
            }

            issuer = found.Value;
        }

        var existing = await ports.Store.EventsOfReceivedAsync(CompanyId, document.Id, ct);
        return await RadianWorkflow.SendAsync(ports, issuer, RadianWorkflow.TargetOf(document), request, existing, [], ct);
    }

    protected async Task<Result<RadianEventResponse>> SendOnOwnAsync(DocumentDetail? detail, EventRequest request, CancellationToken ct)
    {
        if (detail is null)
        {
            return DocumentErrors.NotFound;
        }

        var header = detail.Header;
        if (!InvoiceTypes.Contains(header.DocumentTypeId)
            || header.Status is not (DocumentStatus.Accepted or DocumentStatus.AcceptedWithWarnings) || header.Cufe is null)
        {
            return RadianErrors.OwnInvoiceNotAccepted;
        }

        var issuer = await RadianWorkflow.IssuerAsync(ports, CompanyId, ct);
        if (!issuer.IsSuccess)
        {
            return issuer.Error;
        }

        // The buyer's events (030 to 033) are only at the DIAN: GetStatusEvent lists them before the order is checked.
        IReadOnlyList<DianRegisteredEvent>? registered;
        using (var certificate = await ports.Certificates.LoadAsync(CompanyId, issuer.Value.Certificate!.SecretRef, ct))
        {
            registered = await RadianWorkflow.DianEventsAsync(ports, await RadianWorkflow.EventsUrlAsync(ports, CompanyId, ct), certificate, header.Cufe, ct);
        }

        if (registered is null)
        {
            return RadianErrors.DianUnavailable;
        }

        var customer = header.Customer;
        var note = "Manifiesto bajo la gravedad de juramento que transcurridos 3 días hábiles contados desde la creación del Recibo de " +
            $"bienes y servicios, el adquirente {customer.Name} identificado con NIT {customer.IdentificationNumber} no manifestó " +
            "expresamente la aceptación o rechazo de la referida factura, ni reclamó en contra de su contenido.";
        var target = new EventTarget(
            RadianTarget.OwnInvoice,
            new EventParty
            {
                // php-rules 16.3, item 2: the receiver of the 034 is the DIAN, with fixed data and without cbc:TaxLevelCode.
                Name = DianName, Nit = "800197268", Dv = "4", OrganizationTypeCode = "1", RegimeCode = "48", LiabilityCode = null,
                TaxSchemeCode = "01", TaxSchemeName = "IVA",
            },
            new EventDocumentReference(
                header.FullNumber, header.Cufe, detail.UuidAlgorithm ?? "CUFE-SHA384", RadianWorkflow.DocumentTypeCodeOf(header.DocumentTypeId)),
            null,
            header.Id,
            header.Cufe,
            header.IssueDate,
            header.Totals.PayableAmount,
            header.Totals.TaxInclusiveAmount - header.Totals.LineExtensionAmount);
        var existing = await ports.Store.EventsOfDocumentAsync(CompanyId, header.Id, ct);
        var dianEvents = registered.Select(e => new RegisteredEvent(e.Code, e.EffectiveDate)).ToList();
        return await RadianWorkflow.SendAsync(ports, issuer.Value, target, request with { Note = note }, existing, dianEvents, ct);
    }

    protected Task<DocumentDetail?> OwnByIdAsync(Guid id, CancellationToken ct) => reads.FindByIdAsync(CompanyId, id, ct);

    protected static EventRequest RequestOf(SendEventBody body) => new(
        RadianCatalog.ByCode(body.EventCode)!,
        body.EventCode!.Trim() == RadianCatalog.Claim ? RadianCatalog.ClaimReason(body.ClaimReason) : null,
        EventBodies.Person(body.IssuerParty));

}

internal sealed class SendReceivedDocumentEventHandler(
    ITenantContext tenant, IRadianStore store, IIssuerContextReads issuers, IDocumentCatalogs catalogs, ICertificateStore certificates,
    ISecretProtector secrets, IDianServiceUrls urls, IDianWebService dian, IPlanGate plans, IClock clock, IDocumentReads reads,
    ILogger<SendReceivedDocumentEventHandler> logger)
    : RadianEventHandlerBase(new RadianPorts(tenant, store, issuers, catalogs, certificates, secrets, urls, dian, plans, clock, logger), reads),
        IHandler<SendReceivedDocumentEventCommand, RadianEventResponse>
{
    public async Task<Result<RadianEventResponse>> HandleAsync(SendReceivedDocumentEventCommand request, CancellationToken ct) =>
        await Ports.Store.FindReceivedAsync(CompanyId, request.Id, ct) is { } document
            ? await SendOnReceivedAsync(document, RequestOf(request.Body), null, ct)
            : RadianErrors.ReceivedNotFound;
}

internal sealed class SendDocumentEventHandler(
    ITenantContext tenant, IRadianStore store, IIssuerContextReads issuers, IDocumentCatalogs catalogs, ICertificateStore certificates,
    ISecretProtector secrets, IDianServiceUrls urls, IDianWebService dian, IPlanGate plans, IClock clock, IDocumentReads reads,
    ILogger<SendDocumentEventHandler> logger)
    : RadianEventHandlerBase(new RadianPorts(tenant, store, issuers, catalogs, certificates, secrets, urls, dian, plans, clock, logger), reads),
        IHandler<SendDocumentEventCommand, RadianEventResponse>
{
    public async Task<Result<RadianEventResponse>> HandleAsync(SendDocumentEventCommand request, CancellationToken ct) =>
        await SendOnOwnAsync(await OwnByIdAsync(request.Id, ct), RequestOf(request.Body), ct);
}

internal sealed class ListDocumentEventsHandler(IRadianStore store, IDocumentReads reads, ITenantContext tenant)
    : IHandler<ListDocumentEventsQuery, DocumentEventListResponse>
{
    public async Task<Result<DocumentEventListResponse>> HandleAsync(ListDocumentEventsQuery request, CancellationToken ct) =>
        tenant.CompanyId is { } companyId && await reads.FindByIdAsync(companyId, request.Id, ct) is not null
            ? new DocumentEventListResponse
            {
                Data = RadianMapping.Ordered(await store.EventsOfDocumentAsync(companyId, request.Id, ct)).Select(RadianMapping.Event).ToList(),
            }
            : DocumentErrors.NotFound;
}

internal sealed class GetDianEventsHandler(
    ITenantContext tenant, IRadianStore store, IIssuerContextReads issuers, IDocumentCatalogs catalogs, ICertificateStore certificates,
    ISecretProtector secrets, IDianServiceUrls urls, IDianWebService dian, IPlanGate plans, IClock clock, IDocumentReads reads,
    ILogger<GetDianEventsHandler> logger) : IHandler<GetDianEventsQuery, DianEventListResponse>
{
    private readonly RadianPorts ports = new(tenant, store, issuers, catalogs, certificates, secrets, urls, dian, plans, clock, logger);

    public async Task<Result<DianEventListResponse>> HandleAsync(GetDianEventsQuery request, CancellationToken ct)
    {
        var companyId = tenant.CompanyId ?? throw new InvalidOperationException("The tenant decorator guarantees a company.");
        var cufe = await OwnCufeAsync(companyId, request, ct);
        if (cufe is null)
        {
            return request.ReceivedDocumentId is not null ? RadianErrors.ReceivedNotFound : DocumentErrors.NotFound;
        }

        var issuer = await RadianWorkflow.IssuerAsync(ports, companyId, ct);
        if (!issuer.IsSuccess)
        {
            return issuer.Error;
        }

        IReadOnlyList<DianRegisteredEvent>? events;
        using (var certificate = await certificates.LoadAsync(companyId, issuer.Value.Certificate!.SecretRef, ct))
        {
            events = await RadianWorkflow.DianEventsAsync(ports, await RadianWorkflow.EventsUrlAsync(ports, companyId, ct), certificate, cufe, ct);
        }

        return events is null
            ? RadianErrors.DianUnavailable
            : new DianEventListResponse
            {
                Success = true,
                Message = "Consulta de eventos generada con exito",
                Cufe = cufe,
                Events = events.Select(e => new DianEventResponse
                {
                    EventNumber = e.Number, DianCode = e.Code, Description = e.Description, Date = e.EffectiveDate, Time = e.EffectiveTime,
                    Cude = e.Cude,
                }).ToList(),
            };
    }

    // Only a CUFE of the company is queried (php-rules 7, row 17): an own document or a received one.
    private async Task<string?> OwnCufeAsync(Guid companyId, GetDianEventsQuery request, CancellationToken ct)
    {
        if (request.DocumentId is { } documentId)
        {
            return (await reads.FindByIdAsync(companyId, documentId, ct))?.Header.Cufe;
        }

        return request.ReceivedDocumentId is { } receivedId ? (await store.FindReceivedAsync(companyId, receivedId, ct))?.Cufe : null;
    }
}
