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
using ImpiFact.Application.Features.Documents.Queries.Download;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Features.Documents.Radian;

/// <summary>
/// <c>POST /api/v1/received-documents</c> and the PHP's <c>load-document-by-cufe</c>: downloads an invoice addressed to the
/// company from the DIAN by its CUFE (GetXmlByDocumentKey) and stores it with its XML. With <see cref="Save"/> false (only
/// the PHP route) it answers the data without storing anything.
/// </summary>
public sealed record LoadReceivedDocumentCommand : ICommand
{
    [JsonPropertyName("cufe")]
    public string? Cufe { get; init; }

    /// <summary>PHP <c>save</c>: store the document. The Postman collection sends <c>saved</c>, which is read the same way.</summary>
    [JsonIgnore]
    public bool Save { get; init; } = true;
}

/// <summary>What loading found: the document, whether it was stored now and whether it already existed.</summary>
public sealed record LoadReceivedDocumentResult(ReceivedDocumentResponse Document, bool Saved, bool AlreadyExisted);

/// <summary><c>GET /api/v1/received-documents</c>: the company's received documents, newest issue date first.</summary>
public sealed record ListReceivedDocumentsQuery(string? SupplierNit, DateOnly? From, DateOnly? To, int? Limit, int? Offset);

/// <summary><c>GET /api/v1/received-documents/{id}</c>: a received document with its events.</summary>
public sealed record GetReceivedDocumentQuery(Guid Id);

/// <summary><c>GET /api/v1/received-documents/{id}/xml</c>: the invoice XML as the DIAN or the seller delivered it.</summary>
public sealed record GetReceivedDocumentXmlQuery(Guid Id);

/// <summary>
/// PHP <c>GET receivedfile/{nit}/{file}</c>: the XML of a received document by the company NIT and <c>{cufe}.xml</c>. Another
/// NIT answers 403 and an unknown file 404, without revealing documents of other companies.
/// </summary>
public sealed record DownloadReceivedFileQuery(string Nit, string File);

internal sealed class LoadReceivedDocumentCommandValidator : AbstractValidator<LoadReceivedDocumentCommand>
{
    public LoadReceivedDocumentCommandValidator()
    {
        RuleFor(command => command.Cufe)
            .Cascade(CascadeMode.Stop)
            .Must(cufe => !string.IsNullOrWhiteSpace(cufe)).WithMessage("El campo cufe es requerido.")
            .Must(cufe => cufe!.Trim().Length <= 96 && cufe!.Trim().All(char.IsAsciiHexDigit))
            .WithMessage("El campo cufe debe ser un código hexadecimal de hasta 96 caracteres.")
            .OverridePropertyName("cufe");
    }
}

internal sealed class ListReceivedDocumentsQueryValidator : AbstractValidator<ListReceivedDocumentsQuery>
{
    public const int MaxLimit = 200;

    public ListReceivedDocumentsQueryValidator()
    {
        RuleFor(query => query.Limit).InclusiveBetween(1, MaxLimit).When(query => query.Limit is not null)
            .WithMessage($"limit debe estar entre 1 y {MaxLimit}.").OverridePropertyName("limit");
        RuleFor(query => query.Offset).GreaterThanOrEqualTo(0).When(query => query.Offset is not null)
            .WithMessage("offset no puede ser negativo.").OverridePropertyName("offset");
        RuleFor(query => query.SupplierNit).Must(nit => nit!.All(char.IsAsciiDigit) && nit!.Length <= 20)
            .When(query => !string.IsNullOrEmpty(query.SupplierNit))
            .WithMessage("supplier_nit debe tener solo dígitos.").OverridePropertyName("supplier_nit");
        RuleFor(query => query).Must(query => query.From is null || query.To is null || query.From <= query.To)
            .WithMessage("from debe ser anterior o igual a to.").OverridePropertyName("from");
    }
}

internal sealed class LoadReceivedDocumentHandler(
    ITenantContext tenant,
    IRadianStore store,
    IIssuerContextReads issuers,
    IDocumentCatalogs catalogs,
    ICertificateStore certificates,
    ISecretProtector secrets,
    IDianServiceUrls urls,
    IDianWebService dian,
    IPlanGate plans,
    IClock clock,
    ILogger<LoadReceivedDocumentHandler> logger) : IHandler<LoadReceivedDocumentCommand, LoadReceivedDocumentResult>
{
    private readonly RadianPorts ports = new(tenant, store, issuers, catalogs, certificates, secrets, urls, dian, plans, clock, logger);

    public async Task<Result<LoadReceivedDocumentResult>> HandleAsync(LoadReceivedDocumentCommand request, CancellationToken ct)
    {
        var companyId = tenant.CompanyId ?? throw new InvalidOperationException("The tenant decorator guarantees a company.");
        var cufe = request.Cufe!.Trim().ToLowerInvariant();
        if (await store.FindReceivedByCufeAsync(companyId, cufe, ct) is { } existing)
        {
            return new LoadReceivedDocumentResult(
                RadianMapping.Received(existing, await store.EventsOfReceivedAsync(companyId, existing.Id, ct)), Saved: false, AlreadyExisted: true);
        }

        var issuer = await RadianWorkflow.IssuerAsync(ports, companyId, ct);
        if (!issuer.IsSuccess)
        {
            return issuer.Error;
        }

        var downloaded = await RadianWorkflow.DownloadAsync(ports, issuer.Value, cufe, ct);
        if (!downloaded.IsSuccess)
        {
            return downloaded.Error;
        }

        var (invoice, xml) = downloaded.Value;
        if (!request.Save)
        {
            var preview = RadianMapping.Received(Preview(companyId, invoice), []);
            return new LoadReceivedDocumentResult(preview, Saved: false, AlreadyExisted: false);
        }

        var document = await RadianWorkflow.AddReceivedAsync(ports, companyId, invoice, xml, ct);
        return new LoadReceivedDocumentResult(RadianMapping.Received(document, []), Saved: true, AlreadyExisted: false);
    }

    private static Domain.Documents.ReceivedDocument Preview(Guid companyId, Dian.Radian.ReceivedInvoice invoice) => new()
    {
        CompanyId = companyId,
        SupplierIdentificationNumber = invoice.SupplierNit,
        SupplierDv = invoice.SupplierDv,
        SupplierName = invoice.SupplierName,
        SupplierEmail = invoice.SupplierEmail,
        DocumentTypeId = RadianWorkflow.DocumentTypeIdOf(invoice.InvoiceTypeCode) ?? 1,
        Prefix = invoice.Prefix,
        Number = invoice.Number,
        Cufe = invoice.Cufe,
        IssueDate = invoice.IssueDate,
        TypeEnvironmentId = invoice.Environment == 1 ? 1 : 2,
        LineExtensionAmount = invoice.LineExtensionAmount,
        TaxExclusiveAmount = invoice.TaxExclusiveAmount,
        AllowanceTotalAmount = invoice.AllowanceTotalAmount,
        TaxAmount = invoice.TaxInclusiveAmount - invoice.LineExtensionAmount,
        PayableAmount = invoice.PayableAmount,
    };
}

internal sealed class ListReceivedDocumentsHandler(IRadianStore store, ITenantContext tenant)
    : IHandler<ListReceivedDocumentsQuery, ReceivedDocumentListResponse>
{
    private const int DefaultLimit = 50;

    public async Task<Result<ReceivedDocumentListResponse>> HandleAsync(ListReceivedDocumentsQuery request, CancellationToken ct)
    {
        var companyId = tenant.CompanyId ?? throw new InvalidOperationException("The tenant decorator guarantees a company.");
        var limit = request.Limit ?? DefaultLimit;
        var offset = request.Offset ?? 0;
        var filter = new ReceivedDocumentFilter(
            companyId, string.IsNullOrEmpty(request.SupplierNit) ? null : request.SupplierNit, request.From, request.To, offset, limit);
        var rows = await store.ListReceivedAsync(filter, ct);
        var events = (await store.EventsOfReceivedAsync(companyId, rows.Select(row => row.Id).ToList(), ct))
            .ToLookup(e => e.ReceivedDocumentId);
        return new ReceivedDocumentListResponse
        {
            Data = rows.Select(row => RadianMapping.Received(row, events[row.Id])).ToList(),
            TotalCount = await store.CountReceivedAsync(filter, ct),
            Limit = limit,
            Offset = offset,
        };
    }
}

internal sealed class GetReceivedDocumentHandler(IRadianStore store, ITenantContext tenant)
    : IHandler<GetReceivedDocumentQuery, ReceivedDocumentResponse>
{
    public async Task<Result<ReceivedDocumentResponse>> HandleAsync(GetReceivedDocumentQuery request, CancellationToken ct) =>
        tenant.CompanyId is { } companyId && await store.FindReceivedAsync(companyId, request.Id, ct) is { } document
            ? RadianMapping.Received(document, await store.EventsOfReceivedAsync(companyId, document.Id, ct))
            : RadianErrors.ReceivedNotFound;
}

internal sealed class GetReceivedDocumentXmlHandler(IRadianStore store, ITenantContext tenant)
    : IHandler<GetReceivedDocumentXmlQuery, DocumentFileContent>
{
    public async Task<Result<DocumentFileContent>> HandleAsync(GetReceivedDocumentXmlQuery request, CancellationToken ct) =>
        tenant.CompanyId is { } companyId && await store.FindReceivedAsync(companyId, request.Id, ct) is { } document
            ? await ReceivedXml.ReadAsync(store, companyId, document, ct)
            : RadianErrors.ReceivedNotFound;
}

internal sealed class DownloadReceivedFileHandler(IRadianStore store, IDocumentReads reads, ITenantContext tenant)
    : IHandler<DownloadReceivedFileQuery, DocumentFileContent>
{
    public async Task<Result<DocumentFileContent>> HandleAsync(DownloadReceivedFileQuery request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId || await reads.FindCompanyAsync(companyId, ct) is not { } company
            || !string.Equals(company.IdentificationNumber, request.Nit, StringComparison.Ordinal))
        {
            return Queries.DocumentErrors.ForeignNit;
        }

        var file = request.File.Trim();
        return file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            && await store.FindReceivedByCufeAsync(companyId, file[..^4].ToLowerInvariant(), ct) is { } document
            ? await ReceivedXml.ReadAsync(store, companyId, document, ct)
            : RadianErrors.ReceivedNotFound;
    }
}

internal static class ReceivedXml
{
    public static async Task<Result<DocumentFileContent>> ReadAsync(
        IRadianStore store, Guid companyId, Domain.Documents.ReceivedDocument document, CancellationToken ct) =>
        document.XmlFileKey is { } key && await store.ReadFileAsync(companyId, key, ct) is { } bytes
            ? new DocumentFileContent(document.Cufe + ".xml", "application/xml", bytes)
            : Queries.DocumentErrors.FileNotFound;
}
