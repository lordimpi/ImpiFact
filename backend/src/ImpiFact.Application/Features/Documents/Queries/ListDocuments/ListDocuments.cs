using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Documents.Queries.ListDocuments;

/// <summary>
/// <c>GET /api/v1/documents</c>: documentos de la empresa de la credencial, del más reciente al más antiguo. Los filtros
/// son los de <see cref="IDocumentFilters"/>.
/// </summary>
/// <param name="DocumentTypeId">Tipo de documento (1 factura, 4 nota crédito, 5 nota débito…).</param>
/// <param name="Status">Uno o varios nombres de estado separados por coma, sin distinguir mayúsculas (<c>Accepted</c>, <c>Rejected</c>…).</param>
/// <param name="From">Primer día de emisión incluido, <c>yyyy-MM-dd</c> en hora Colombia.</param>
/// <param name="To">Último día de emisión incluido.</param>
/// <param name="Cursor">El <c>next_cursor</c> de la página anterior.</param>
/// <param name="Limit">Documentos por página; 50 si no se indica, máximo 200.</param>
/// <param name="Kind">Familia: <c>invoice</c>, <c>credit_note</c> o <c>debit_note</c>.</param>
/// <param name="Customer">Texto que contiene el número de identificación o el nombre del adquiriente.</param>
/// <param name="Number">Número completo con prefijo o solo los dígitos.</param>
/// <param name="IncludeTotal">Si es verdadero, la respuesta trae <c>total_count</c> (cuesta una consulta más).</param>
public sealed record ListDocumentsQuery(
    int? DocumentTypeId,
    string? Status,
    string? From,
    string? To,
    string? Cursor,
    int? Limit,
    string? Kind = null,
    string? Customer = null,
    string? Number = null,
    bool IncludeTotal = false) : IDocumentFilters;

/// <summary>
/// Pide una fila más de las que muestra: si llega, hay página siguiente y su cursor es la posición del último
/// documento mostrado. Con <c>include_total</c> cuenta además todas las filas que cumplen los filtros, sin importar el
/// cursor, para que el panel pueda mostrar el número de páginas.
/// </summary>
internal sealed class ListDocumentsHandler(IDocumentReads reads, ITenantContext tenant) : IHandler<ListDocumentsQuery, DocumentListResponse>
{
    public async Task<Result<DocumentListResponse>> HandleAsync(ListDocumentsQuery request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId)
        {
            return DocumentErrors.ForeignNit;
        }

        var limit = request.Limit ?? ListDocumentsQueryValidator.DefaultLimit;
        DocumentKey? after = DocumentCursor.TryDecode(request.Cursor, out var key) ? key : null;
        var filter = DocumentFilters.ToFilter(request, companyId, after, limit + 1);
        var rows = await reads.ListAsync(filter, ct);
        long? total = request.IncludeTotal ? await reads.CountAsync(filter, ct) : null;

        var page = rows.Take(limit).ToList();
        var last = page.LastOrDefault();
        return new DocumentListResponse
        {
            Items = page.Select(DocumentMapping.Summary).ToList(),
            NextCursor = rows.Count > limit && last is not null ? DocumentCursor.Encode(new DocumentKey(last.IssuedAt, last.Id)) : null,
            TotalCount = total,
        };
    }
}

internal sealed class ListDocumentsQueryValidator : AbstractValidator<ListDocumentsQuery>
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 200;

    public ListDocumentsQueryValidator()
    {
        RuleFor(query => query.Limit)
            .GreaterThanOrEqualTo(1).WithMessage(QueryMessages.MinNumber("limit", 1))
            .LessThanOrEqualTo(MaxLimit).WithMessage(QueryMessages.MaxNumber("limit", MaxLimit))
            .OverridePropertyName("limit");
        Include(new DocumentFiltersValidator());
        RuleFor(query => query.Cursor)
            .Must(cursor => cursor is null || DocumentCursor.TryDecode(cursor, out _))
            .OverridePropertyName("cursor")
            .WithMessage(QueryMessages.Invalid("cursor"));
    }
}
