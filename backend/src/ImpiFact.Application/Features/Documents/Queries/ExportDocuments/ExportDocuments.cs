using System.Runtime.CompilerServices;
using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Documents.Queries.ExportDocuments;

/// <summary>
/// <c>GET /api/v1/documents/export.csv</c>: los documentos del listado con los mismos filtros, en un archivo CSV
/// (valores separados por punto y coma) para abrir en una hoja de cálculo. Sin cursor ni límite: trae todas las filas
/// hasta <see cref="MaxRows"/>. <c>GET /api/v1/documents/export.xlsx</c> runs the same query and writes an Excel workbook.
/// </summary>
public sealed record ExportDocumentsQuery(
    int? DocumentTypeId,
    string? Kind,
    string? Status,
    string? From,
    string? To,
    string? Customer,
    string? Number) : IDocumentFilters
{
    /// <summary>Máximo de filas de una exportación; para más, se acota por fechas u otros filtros.</summary>
    public const int MaxRows = 50_000;
}

/// <summary>
/// Exportación lista para escribir: <see cref="Count"/> filas que se leen de la base mientras se escriben, sin
/// cargarlas todas en memoria. The CSV and the Excel workbook share these rows and their columns.
/// </summary>
public sealed class DocumentExport(long count, IAsyncEnumerable<DocumentSummary> rows, ISpreadsheetWriter spreadsheets)
{
    /// <summary>Media type of the Excel (.xlsx) workbook.</summary>
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Name of the single sheet of the Excel workbook.</summary>
    public const string SheetName = "Documentos";

    /// <summary>Filas que cumplían los filtros al contarlas (las que se escriben pueden variar si entran documentos nuevos).</summary>
    public long Count { get; } = count;

    /// <summary>Nombre sugerido del archivo descargado.</summary>
    public string FileName => "documentos.csv";

    /// <summary>Suggested name of the downloaded Excel file.</summary>
    public string XlsxFileName => "documentos.xlsx";

    /// <summary>Escribe el CSV en <paramref name="output"/> (por ejemplo, el cuerpo de la respuesta HTTP).</summary>
    public Task WriteCsvAsync(Stream output, CancellationToken ct) => DocumentCsv.WriteAsync(output, rows, ct);

    /// <summary>Writes the Excel workbook to <paramref name="output"/>, with the CSV columns as typed cells.</summary>
    public Task WriteXlsxAsync(Stream output, CancellationToken ct) =>
        spreadsheets.WriteAsync(output, SheetName, DocumentExportColumns.Layouts, Values(ct), ct);

    private async IAsyncEnumerable<object?[]> Values([EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var row in rows.WithCancellation(ct))
        {
            yield return DocumentExportColumns.Values(row);
        }
    }
}

/// <summary>
/// Cuenta primero las filas que cumplen los filtros: si pasan del máximo responde 422 antes de escribir nada, así el
/// cliente recibe un error claro y no un archivo cortado. Si no, devuelve la lectura por partes, con el máximo como
/// tope por si entran documentos entre el conteo y la lectura.
/// </summary>
internal sealed class ExportDocumentsHandler(IDocumentReads reads, ITenantContext tenant, ISpreadsheetWriter spreadsheets) : IHandler<ExportDocumentsQuery, DocumentExport>
{
    private const int MaxRows = ExportDocumentsQuery.MaxRows;

    public async Task<Result<DocumentExport>> HandleAsync(ExportDocumentsQuery request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId)
        {
            return DocumentErrors.ForeignNit;
        }

        var filter = DocumentFilters.ToFilter(request, companyId, after: null, take: MaxRows);
        var count = await reads.CountAsync(filter, ct);
        if (count > MaxRows)
        {
            return DocumentErrors.ExportTooLarge(count, MaxRows);
        }

        return new DocumentExport(count, reads.StreamAsync(filter, ct), spreadsheets);
    }
}

internal sealed class ExportDocumentsQueryValidator : AbstractValidator<ExportDocumentsQuery>
{
    public ExportDocumentsQueryValidator() => Include(new DocumentFiltersValidator());
}
