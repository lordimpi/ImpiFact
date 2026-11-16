using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Features.Documents.Queries.ListDocuments;

/// <summary>
/// <c>GET /api/v1/documents</c>: documentos de la empresa de la credencial, del más reciente al más antiguo.
/// </summary>
/// <param name="DocumentTypeId">Tipo de documento (1 factura, 4 nota crédito, 5 nota débito…).</param>
/// <param name="Status">Nombre del estado, sin distinguir mayúsculas (<c>Accepted</c>, <c>Rejected</c>…).</param>
/// <param name="From">Primer día de emisión incluido, <c>yyyy-MM-dd</c> en hora Colombia.</param>
/// <param name="To">Último día de emisión incluido.</param>
/// <param name="Cursor">El <c>next_cursor</c> de la página anterior.</param>
/// <param name="Limit">Documentos por página; 50 si no se indica, máximo 200.</param>
public sealed record ListDocumentsQuery(int? DocumentTypeId, string? Status, string? From, string? To, string? Cursor, int? Limit);

/// <summary>
/// Pide una fila más de las que muestra: si llega, hay página siguiente y su cursor es la posición del último
/// documento mostrado.
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
        DateTimeOffset? issuedFrom = ColombiaDates.TryParse(request.From, out var from) ? ColombiaDates.StartOf(from) : null;
        DateTimeOffset? issuedUntil = ColombiaDates.TryParse(request.To, out var to) ? ColombiaDates.StartOf(to.AddDays(1)) : null;
        var rows = await reads.ListAsync(
            new DocumentListFilter(companyId, request.DocumentTypeId, ListDocumentsQueryValidator.ParseStatus(request.Status), issuedFrom, issuedUntil, after, limit + 1),
            ct);

        var page = rows.Take(limit).ToList();
        var last = page.LastOrDefault();
        return new DocumentListResponse
        {
            Items = page.Select(DocumentMapping.Summary).ToList(),
            NextCursor = rows.Count > limit && last is not null ? DocumentCursor.Encode(new DocumentKey(last.IssuedAt, last.Id)) : null,
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
        RuleFor(query => query.Status)
            .Must(status => status is null || ParseStatus(status) is not null)
            .OverridePropertyName("status")
            .WithMessage(QueryMessages.Invalid("status"));
        RuleFor(query => query.From)
            .Must(value => value is null || ColombiaDates.TryParse(value, out _))
            .OverridePropertyName("from")
            .WithMessage(QueryMessages.Date("from"));
        RuleFor(query => query.To)
            .Must(value => value is null || ColombiaDates.TryParse(value, out _))
            .OverridePropertyName("to")
            .WithMessage(QueryMessages.Date("to"));
        RuleFor(query => query)
            .Must(query => !ColombiaDates.TryParse(query.From, out var from) || !ColombiaDates.TryParse(query.To, out var to) || from <= to)
            .OverridePropertyName("to")
            .WithMessage(QueryMessages.AfterOrEqual("to", "from"));
        RuleFor(query => query.Cursor)
            .Must(cursor => cursor is null || DocumentCursor.TryDecode(cursor, out _))
            .OverridePropertyName("cursor")
            .WithMessage(QueryMessages.Invalid("cursor"));
    }

    /// <summary>
    /// Estado por su nombre, sin distinguir mayúsculas. No se aceptan números: <c>Enum.TryParse</c> aceptaría
    /// cualquier número, incluso uno que no es un estado.
    /// </summary>
    public static DocumentStatus? ParseStatus(string? status) =>
        status is null ? null : Enum.GetValues<DocumentStatus>().Cast<DocumentStatus?>()
            .FirstOrDefault(value => string.Equals(value.ToString(), status, StringComparison.OrdinalIgnoreCase));
}
