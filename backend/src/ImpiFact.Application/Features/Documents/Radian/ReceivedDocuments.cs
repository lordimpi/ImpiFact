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
/// <c>POST /api/v1/received-documents</c> (the PHP's <c>load-document-by-cufe</c>): downloads an invoice addressed to the
/// company from the DIAN by its CUFE (GetXmlByDocumentKey) and stores it with its XML.
/// </summary>
public sealed record LoadReceivedDocumentCommand : ICommand
{
    [JsonPropertyName("cufe")]
    public string? Cufe { get; init; }
}

/// <summary>What loading found: the document and whether it already existed.</summary>
public sealed record LoadReceivedDocumentResult(ReceivedDocumentResponse Document, bool AlreadyExisted);

/// <summary><c>GET /api/v1/received-documents</c>: the company's received documents, newest issue date first.</summary>
public sealed record ListReceivedDocumentsQuery(string? SupplierNit, DateOnly? From, DateOnly? To, int? Limit, int? Offset);

/// <summary><c>GET /api/v1/received-documents/{id}</c>: a received document with its events.</summary>
public sealed record GetReceivedDocumentQuery(Guid Id);

/// <summary><c>GET /api/v1/received-documents/{id}/xml</c>: the invoice XML as the DIAN or the seller delivered it.</summary>
public sealed record GetReceivedDocumentXmlQuery(Guid Id);

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
                RadianMapping.Received(existing, await store.EventsOfReceivedAsync(companyId, existing.Id, ct)), AlreadyExisted: true);
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
        var document = await RadianWorkflow.AddReceivedAsync(ports, companyId, invoice, xml, ct);
        return new LoadReceivedDocumentResult(RadianMapping.Received(document, []), AlreadyExisted: false);
    }
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

internal static class ReceivedXml
{
    public static async Task<Result<DocumentFileContent>> ReadAsync(
        IRadianStore store, Guid companyId, Domain.Documents.ReceivedDocument document, CancellationToken ct) =>
        document.XmlFileKey is { } key && await store.ReadFileAsync(companyId, key, ct) is { } bytes
            ? new DocumentFileContent(document.Cufe + ".xml", "application/xml", bytes)
            : Queries.DocumentErrors.FileNotFound;
}
