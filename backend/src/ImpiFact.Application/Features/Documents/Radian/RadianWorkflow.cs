using System.Security.Cryptography.X509Certificates;
using System.Text;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Billing;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Dian;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Dian.Cufe;
using ImpiFact.Dian.Radian;
using ImpiFact.Dian.Signing;
using ImpiFact.Dian.Soap;
using ImpiFact.Dian.Ubl;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Features.Documents.Radian;

/// <summary>The ports the RADIAN use cases share (built in each handler, like the issue ports).</summary>
internal sealed record RadianPorts(
    ITenantContext Tenant,
    IRadianStore Store,
    IIssuerContextReads Issuers,
    IDocumentCatalogs Catalogs,
    ICertificateStore Certificates,
    ISecretProtector Secrets,
    IDianServiceUrls Urls,
    IDianWebService Dian,
    IPlanGate Plans,
    IClock Clock,
    ILogger Logger);

/// <summary>Who receives the event and which invoice it is about.</summary>
internal sealed record EventTarget(
    RadianTarget Kind,
    EventParty Receiver,
    EventDocumentReference Reference,
    Guid? ReceivedDocumentId,
    Guid? DocumentId,
    string? InvoiceCufe,
    DateOnly? InvoiceDate,
    decimal? InvoiceTotal,
    decimal? InvoiceTax);

/// <summary>What the company asks to send: the event, the claim reason of a 031 and the person who decides.</summary>
internal sealed record EventRequest(RadianEventType Type, (int Id, string Code, string Name)? ClaimReason, EventPerson? Person, string? Note = null);

/// <summary>
/// The two flows of RADIAN (phase 7a, T6): loading a received invoice from the DIAN by its CUFE, and building, signing and
/// sending an event (Arquitectura detallada, RADIAN section). One SOAP call per request, like the PHP; the answer of the
/// DIAN decides the status and is always stored, accepted or not.
/// </summary>
internal static class RadianWorkflow
{
    /// <summary>Invoice type (<c>type_documents</c> id) whose context gives the company, software and certificate.</summary>
    private const int InvoiceDocumentType = 1;

    private const string Rule90 = "Regla: 90, Rechazo: Documento procesado anteriormente.";

    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>InvoiceTypeCode of a received invoice → <c>type_documents</c> id (01, 02, 03 and the type 04, id 12).</summary>
    public static int? DocumentTypeIdOf(string invoiceTypeCode) => invoiceTypeCode switch
    {
        "01" => 1,
        "02" => 2,
        "03" => 3,
        "04" => 12,
        _ => null,
    };

    public static string DocumentTypeCodeOf(int documentTypeId) => documentTypeId switch
    {
        2 => "02",
        3 => "03",
        12 => "04",
        _ => "01",
    };

    /// <summary>The company, certificate and invoice software, checked as the PHP does before calling the DIAN.</summary>
    public static async Task<Result<IssuerContext>> IssuerAsync(RadianPorts ports, Guid companyId, CancellationToken ct)
    {
        var issuer = await ports.Issuers.GetAsync(companyId, InvoiceDocumentType, DocumentKind.Invoice, ct);
        if (issuer is null || !issuer.Company.IsActive)
        {
            return IssueErrors.CompanyInactive;
        }

        if (issuer.Certificate is not { } certificate)
        {
            return IssueErrors.CertificateMissing;
        }

        if (ColombiaTime.DaysLeft(ColombiaTime.Today(certificate.NotAfter), ports.Clock.UtcNow) <= 0)
        {
            return IssueErrors.CertificateExpired;
        }

        return issuer.Software is null ? RadianErrors.SoftwareMissing : issuer;
    }

    /// <summary>
    /// Service URL for the events: the environment of the events family (the PHP's <c>url_event</c>, which follows
    /// <c>event_type_environment_id</c>), habilitación when the company never chose one.
    /// </summary>
    public static async Task<string> EventsUrlAsync(RadianPorts ports, Guid companyId, CancellationToken ct)
    {
        var events = await ports.Issuers.GetAsync(companyId, InvoiceDocumentType, DocumentKind.Events, ct);
        return ports.Urls.ForEnvironment(events?.TypeEnvironmentId ?? 2);
    }

    /// <summary>
    /// Downloads an invoice from the DIAN by its CUFE (GetXmlByDocumentKey) and reads it. The invoice must be addressed to
    /// the company (its buyer NIT) and carry the CUFE that was asked for.
    /// </summary>
    public static async Task<Result<(ReceivedInvoice Invoice, byte[] Xml)>> DownloadAsync(
        RadianPorts ports, IssuerContext issuer, string cufe, CancellationToken ct)
    {
        var companyId = issuer.Company.Id;
        DianCall call;
        using (var certificate = await ports.Certificates.LoadAsync(companyId, issuer.Certificate!.SecretRef, ct))
        {
            call = await ports.Dian.CallAsync(new GetXmlByDocumentKeyRequest(cufe), await EventsUrlAsync(ports, companyId, ct), certificate, ct);
        }

        ports.Logger.LogInformation(
            "DIAN GetXmlByDocumentKey for company {CompanyId} ended as {Outcome} with HTTP {HttpStatus} in {DurationMs} ms.",
            companyId, call.Outcome, call.HttpStatus, (int)call.Duration.TotalMilliseconds);
        switch (call)
        {
            case { Outcome: DianCallOutcome.Answered, Response: DianXmlDocumentResponse { Code: "100", Xml: { } xml } }:
                var invoice = ReceivedInvoiceReader.Read(xml);
                if (invoice is null || DocumentTypeIdOf(invoice.InvoiceTypeCode) is null
                    || !string.Equals(invoice.Cufe, cufe, StringComparison.OrdinalIgnoreCase))
                {
                    return RadianErrors.NotAnInvoice;
                }

                return invoice.CustomerNit == issuer.Company.IdentificationNumber
                    ? (invoice, xml)
                    : RadianErrors.WrongBuyer(issuer.Company.IdentificationNumber);
            case { Outcome: DianCallOutcome.Answered, Response: DianXmlDocumentResponse other }:
                return RadianErrors.DianNotFound(other.Message);
            case { Outcome: DianCallOutcome.Answered, Response: DianSoapFaultResponse fault }:
                return RadianErrors.DianFault(fault.Reason);
            default:
                return RadianErrors.DianUnavailable;
        }
    }

    /// <summary>
    /// Adds the received document of <paramref name="invoice"/> with its XML stored under a key generated by the server
    /// (never the name a client sends, php-rules 16.2, item 7).
    /// </summary>
    public static async Task<ReceivedDocument> AddReceivedAsync(RadianPorts ports, Guid companyId, ReceivedInvoice invoice, byte[] xml, CancellationToken ct)
    {
        var id = Guid.CreateVersion7();
        var key = await ports.Store.PutFileAsync(companyId, id, xml, "application/xml", ".xml", ct);
        var document = new ReceivedDocument
        {
            Id = id,
            CompanyId = companyId,
            SupplierIdentificationNumber = invoice.SupplierNit,
            SupplierDv = invoice.SupplierDv,
            SupplierName = Truncate(invoice.SupplierName, 255)!,
            SupplierOrganizationTypeCode = Truncate(invoice.SupplierOrganizationTypeCode, 2),
            SupplierRegimeCode = Truncate(invoice.SupplierRegimeCode, 2),
            SupplierEmail = Truncate(invoice.SupplierEmail, 255),
            DocumentTypeId = DocumentTypeIdOf(invoice.InvoiceTypeCode) ?? 1,
            Prefix = Truncate(invoice.Prefix, 10) ?? "",
            Number = Truncate(invoice.Number, 20)!,
            Cufe = invoice.Cufe.ToLowerInvariant(),
            IssueDate = invoice.IssueDate,
            TypeEnvironmentId = invoice.Environment == 1 ? 1 : 2,
            LineExtensionAmount = invoice.LineExtensionAmount,
            TaxExclusiveAmount = invoice.TaxExclusiveAmount,
            AllowanceTotalAmount = invoice.AllowanceTotalAmount,
            TaxAmount = invoice.TaxInclusiveAmount - invoice.LineExtensionAmount,
            PayableAmount = invoice.PayableAmount,
            XmlFileKey = key,
        };
        ports.Store.AddReceived(document);
        return document;
    }

    /// <summary>The receiver and the reference of an event on a received invoice: the seller, as the PHP reads it.</summary>
    public static EventTarget TargetOf(ReceivedDocument document) => new(
        RadianTarget.ReceivedInvoice,
        new EventParty
        {
            Name = document.SupplierName,
            Nit = document.SupplierIdentificationNumber,
            Dv = document.SupplierDv,
            OrganizationTypeCode = document.SupplierOrganizationTypeCode ?? "1",
            RegimeCode = document.SupplierRegimeCode ?? "48",
            // php-rules 16.4, item 5: responsibility 117 (R-99-PN) fixed for the receiver.
            LiabilityCode = "R-99-PN",
            TaxSchemeCode = "01",
            TaxSchemeName = "IVA",
        },
        new EventDocumentReference(
            document.Number, document.Cufe, document.DocumentTypeId == 3 ? "CUDE-SHA384" : "CUFE-SHA384",
            DocumentTypeCodeOf(document.DocumentTypeId)),
        document.Id,
        null,
        document.Cufe,
        document.IssueDate,
        document.PayableAmount,
        document.TaxAmount);

    /// <summary>
    /// Checks the order, builds and signs the event (or reuses the XML of an attempt that got no answer), sends it with
    /// SendEventUpdateStatus and records the answer in <c>document_events</c>.
    /// </summary>
    /// <param name="existing">The events of the target (tracked), to check the order and to reuse the row of the type.</param>
    /// <param name="dianRegistered">Events the DIAN lists for an own invoice; empty for a received one.</param>
    public static async Task<Result<RadianEventResponse>> SendAsync(
        RadianPorts ports,
        IssuerContext issuer,
        EventTarget target,
        EventRequest request,
        IReadOnlyList<DocumentEvent> existing,
        IReadOnlyList<RegisteredEvent> dianRegistered,
        CancellationToken ct)
    {
        var companyId = issuer.Company.Id;
        var now = ports.Clock.UtcNow;
        var registered = existing.Where(e => e.Status == DocumentEventStatus.Accepted)
            .Select(e => new RegisteredEvent(RadianCatalog.ById(e.EventId)?.Code ?? "", ColombiaTime.Today(e.IssuedAt)))
            .Concat(dianRegistered)
            .ToList();
        if (RadianEventRules.Check(request.Type.Code, target.Kind, registered, ColombiaTime.Today(now)) is { } outOfOrder)
        {
            return outOfOrder;
        }

        if (await ports.Plans.CheckIssueAsync(companyId, DocumentKind.Events, ct) is { } refused)
        {
            return refused;
        }

        var row = existing.FirstOrDefault(e => e.EventId == request.Type.Id);
        using var certificate = await ports.Certificates.LoadAsync(companyId, issuer.Certificate!.SecretRef, ct);

        // An attempt that got no answer may be registered: the same signed XML (same CUDE) is sent again, so a rule 90 means
        // this very event. Any other row (rejected) gets a new XML.
        byte[] signedXml;
        string cude;
        string number;
        if (row is { Status: DocumentEventStatus.Sent, XmlFileKey: { } previousKey, Number: { } previousNumber }
            && await ports.Store.ReadFileAsync(companyId, previousKey, ct) is { } previousXml)
        {
            (signedXml, cude, number) = (previousXml, row.Cude, previousNumber);
        }
        else
        {
            var built = RadianEventBuilder.Build(await EventOfAsync(ports, issuer, target, request, now, ct));
            signedXml = Utf8.GetBytes(XmlSigner.Sign(built.Xml, certificate, now));
            (cude, number) = (built.Cude, built.Number);
        }

        var local = ColombiaTime.Local(now);
        var zip = DianZip.Create(
            DianFileNames.EventXmlEntry(request.Type.Code, issuer.Company.IdentificationNumber, local.Year, target.Reference.Number),
            signedXml, local);
        var url = await EventsUrlAsync(ports, companyId, ct);
        var call = await ports.Dian.CallAsync(new SendEventUpdateStatusRequest(Convert.ToBase64String(zip)), url, certificate, ct);
        ports.Logger.LogInformation(
            "DIAN SendEventUpdateStatus {EventCode} for company {CompanyId} ended as {Outcome} with HTTP {HttpStatus} in {DurationMs} ms.",
            request.Type.Code, companyId, call.Outcome, call.HttpStatus, (int)call.Duration.TotalMilliseconds);

        DocumentEventStatus status;
        DianResult? result = null;
        switch (call)
        {
            case { Outcome: DianCallOutcome.Answered, Response: DianResultResponse { Results: [var answer, ..] } }:
                result = answer;
                status = answer.IsValid == true || answer.ErrorMessages.Contains(Rule90)
                    ? DocumentEventStatus.Accepted
                    : DocumentEventStatus.Rejected;
                if (status == DocumentEventStatus.Accepted && answer.IsValid != true && row?.Status != DocumentEventStatus.Sent)
                {
                    // Rule 90 on a new XML: the DIAN already has this event with another CUDE. The real CUDE comes from
                    // GetStatusEvent (php-rules 16.6, item 4); the PHP recalculated one instead.
                    var registeredCude = await RegisteredCudeAsync(ports, url, certificate, target.InvoiceCufe, request.Type.Code, ct);
                    if (registeredCude is null)
                    {
                        status = DocumentEventStatus.Sent;
                    }
                    else
                    {
                        cude = registeredCude;
                    }
                }

                break;
            case { Outcome: DianCallOutcome.Answered, Response: DianSoapFaultResponse fault }:
                return RadianErrors.DianFault(fault.Reason);
            case { Outcome: DianCallOutcome.NoAnswer }:
                status = DocumentEventStatus.Sent;
                break;
            default:
                return RadianErrors.DianUnavailable;
        }

        if (row is null)
        {
            row = new DocumentEvent
            {
                Id = Guid.CreateVersion7(),
                CompanyId = companyId,
                DocumentId = target.DocumentId,
                ReceivedDocumentId = target.ReceivedDocumentId,
                EventId = request.Type.Id,
                Cude = cude,
            };
            ports.Store.AddEvent(row);
        }

        row.Cude = cude;
        row.Number = number;
        row.IssuedAt = now;
        row.Status = status;
        row.RejectionCode = request.ClaimReason?.Code;
        row.TrackId = Truncate(result?.XmlDocumentKey, 64);
        row.DianStatusCode = Truncate(result?.StatusCode, 10);
        row.DianStatusDescription = Truncate(result?.StatusDescription ?? (status == DocumentEventStatus.Sent ? call.Failure : null), 500);
        row.ErrorMessages = result?.ErrorMessages.ToList();
        row.XmlFileKey = await ports.Store.PutFileAsync(companyId, row.Id, signedXml, "application/xml", ".xml", ct);
        if (call.Body is { } body)
        {
            row.ResponseFileKey = await ports.Store.PutFileAsync(companyId, row.Id, Utf8.GetBytes(body), "application/soap+xml", ".xml", ct);
        }

        if (status == DocumentEventStatus.Accepted)
        {
            // qty_docs_radian counts the events the DIAN registers (php-rules 16.7, item 6: Mejora, family «events»).
            await ports.Plans.CountAcceptedAsync(companyId, DocumentKind.Events, ct);
        }

        var accepted = status == DocumentEventStatus.Accepted;
        return new RadianEventResponse
        {
            Success = accepted,
            Message = status switch
            {
                DocumentEventStatus.Accepted => $"ApplicationResponse #{number} generada con éxito",
                DocumentEventStatus.Rejected => "La DIAN rechazó el evento; revise error_messages.",
                _ => "La DIAN no confirmó el evento. Envíelo de nuevo: se reenvía el mismo XML firmado.",
            },
            Cude = cude,
            Event = RadianMapping.Event(row),
            ReceivedDocumentId = target.ReceivedDocumentId,
            DocumentId = target.DocumentId,
            TransmitterId = accepted ? target.Receiver.Nit : null,
            TransmitterName = accepted ? target.Receiver.Name : null,
            ReceiverId = accepted ? issuer.Company.IdentificationNumber : null,
            ReceiverName = accepted ? issuer.Company.Name : null,
            InvoiceNumber = accepted ? target.Reference.Number : null,
            InvoiceDate = accepted ? target.InvoiceDate : null,
            InvoiceCufe = accepted ? target.Reference.Uuid : null,
            InvoiceTotal = accepted ? target.InvoiceTotal : null,
            InvoiceTax = accepted ? target.InvoiceTax : null,
        };
    }

    /// <summary>The events the DIAN has registered on an invoice (GetStatusEvent); null when it did not answer with a list.</summary>
    public static async Task<IReadOnlyList<DianRegisteredEvent>?> DianEventsAsync(
        RadianPorts ports, string url, X509Certificate2 certificate, string cufe, CancellationToken ct)
    {
        var call = await ports.Dian.CallAsync(new GetStatusEventRequest(cufe), url, certificate, ct);
        ports.Logger.LogInformation(
            "DIAN GetStatusEvent ended as {Outcome} with HTTP {HttpStatus} in {DurationMs} ms.",
            call.Outcome, call.HttpStatus, (int)call.Duration.TotalMilliseconds);
        return call is { Outcome: DianCallOutcome.Answered, Response: DianResultResponse { Results: [var result, ..] } }
            ? result.XmlBase64Bytes is { } xml ? RadianEventStatusReader.Read(xml) : []
            : null;
    }

    private static async Task<string?> RegisteredCudeAsync(
        RadianPorts ports, string url, X509Certificate2 certificate, string? cufe, string code, CancellationToken ct) =>
        cufe is null
            ? null
            : (await DianEventsAsync(ports, url, certificate, cufe, ct))?.FirstOrDefault(e => e.Code == code)?.Cude;

    private static async Task<RadianEvent> EventOfAsync(
        RadianPorts ports, IssuerContext issuer, EventTarget target, EventRequest request, DateTimeOffset now, CancellationToken ct)
    {
        var company = issuer.Company;
        var keys = new List<CatalogKey>
        {
            new(DianCatalog.OrganizationType, company.TypeOrganizationId),
            new(DianCatalog.Regime, company.TypeRegimeId),
            new(DianCatalog.Tax, company.TaxId),
        };
        keys.AddRange(issuer.LiabilityIds.Select(id => new CatalogKey(DianCatalog.Liability, id)));
        var catalogs = await ports.Catalogs.FindAsync(keys, ct);
        string Code(DianCatalog catalog, int id) =>
            catalogs.TryGetValue(new CatalogKey(catalog, id), out var item) ? item.Code.Trim() : "";

        var tax = catalogs.GetValueOrDefault(new CatalogKey(DianCatalog.Tax, company.TaxId));
        return new RadianEvent
        {
            Code = request.Type.Code,
            Description = request.Type.Name,
            Rejection = request.ClaimReason is { } reason ? new EventRejection(reason.Code, reason.Name) : null,
            // php-rules 16.4, item 1: the environment of the invoice family, as the PHP (open question in section 18).
            Environment = issuer.TypeEnvironmentId == 1 ? DianEnvironment.Production : DianEnvironment.Testing,
            IssueDate = ColombiaTime.Today(now),
            IssueTime = ColombiaTime.Now(now),
            Note = request.Note,
            Sender = new EventParty
            {
                Name = company.Name,
                Nit = company.IdentificationNumber,
                Dv = company.Dv,
                OrganizationTypeCode = Code(DianCatalog.OrganizationType, company.TypeOrganizationId),
                RegimeCode = Code(DianCatalog.Regime, company.TypeRegimeId),
                LiabilityCode = string.Join(';', issuer.LiabilityIds.Order().Select(id => Code(DianCatalog.Liability, id))),
                TaxSchemeCode = tax?.Code.Trim() ?? "01",
                TaxSchemeName = tax?.Name.Trim() ?? "IVA",
            },
            Receiver = target.Receiver,
            Reference = target.Reference,
            Person = request.Person,
            Software = new SoftwareCredentials(issuer.Software!.Identifier, ports.Secrets.Unprotect(SecretPurposes.SoftwarePin, issuer.Software.PinCipher)),
        };
    }

    private static string? Truncate(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}
