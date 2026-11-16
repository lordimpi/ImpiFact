using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Documents.Queries.GetDocument;

/// <summary><c>GET /api/v1/documents/{id}</c>: detalle de un documento de la empresa de la credencial.</summary>
public sealed record GetDocumentQuery(Guid Id);

/// <summary>
/// <c>GET /api/v1/documents/by-cufe/{cufe}</c>: detalle por CUFE o CUDE (códigos únicos de factura y de documento
/// electrónico), que son únicos dentro de la empresa.
/// </summary>
public sealed record GetDocumentByCufeQuery(string Cufe);

/// <summary>Un documento de otra empresa responde igual que uno que no existe: 404.</summary>
internal sealed class GetDocumentHandler(IDocumentReads reads, ITenantContext tenant) : IHandler<GetDocumentQuery, DocumentDetailResponse>
{
    public async Task<Result<DocumentDetailResponse>> HandleAsync(GetDocumentQuery request, CancellationToken ct) =>
        tenant.CompanyId is { } companyId && await reads.FindByIdAsync(companyId, request.Id, ct) is { } detail
            ? DocumentMapping.Detail(detail)
            : DocumentErrors.NotFound;
}

internal sealed class GetDocumentByCufeHandler(IDocumentReads reads, ITenantContext tenant) : IHandler<GetDocumentByCufeQuery, DocumentDetailResponse>
{
    public async Task<Result<DocumentDetailResponse>> HandleAsync(GetDocumentByCufeQuery request, CancellationToken ct) =>
        tenant.CompanyId is { } companyId && await reads.FindByCufeAsync(companyId, request.Cufe, ct) is { } detail
            ? DocumentMapping.Detail(detail)
            : DocumentErrors.NotFound;
}

internal sealed class GetDocumentByCufeQueryValidator : AbstractValidator<GetDocumentByCufeQuery>
{
    /// <summary>Largo de la columna <c>documents.cufe</c>.</summary>
    public const int MaxLength = 96;

    public GetDocumentByCufeQueryValidator()
    {
        RuleFor(query => query.Cufe)
            .Cascade(CascadeMode.Stop)
            .Must(cufe => !string.IsNullOrWhiteSpace(cufe)).WithMessage(QueryMessages.Required("cufe"))
            .MaximumLength(MaxLength).WithMessage(QueryMessages.MaxLength("cufe", MaxLength))
            .OverridePropertyName("cufe");
    }
}
