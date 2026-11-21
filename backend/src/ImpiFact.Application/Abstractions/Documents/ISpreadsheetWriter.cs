namespace ImpiFact.Application.Abstractions.Documents;

/// <summary>How a spreadsheet cell is typed and formatted.</summary>
public enum SpreadsheetCellKind
{
    /// <summary>Plain text (a <see cref="string"/>); never evaluated as a formula.</summary>
    Text,

    /// <summary>A money amount (a <see cref="decimal"/>), numeric with thousands separator and two decimals.</summary>
    Amount,

    /// <summary>A calendar day (a <see cref="DateOnly"/>), a real date cell shown as dd/mm/yyyy.</summary>
    Date,

    /// <summary>A time of day (a <see cref="TimeOnly"/>), a real time cell shown as hh:mm:ss.</summary>
    Time,
}

/// <summary>A sheet column: bold header, cell kind and width in characters.</summary>
public sealed record SpreadsheetColumn(string Header, SpreadsheetCellKind Kind, double Width);

/// <summary>
/// Writes a single-sheet Excel workbook (.xlsx). Rows are written as they arrive, without holding them all in memory.
/// Each row has one value per column, of the type its <see cref="SpreadsheetCellKind"/> names; a null or empty text
/// value leaves the cell empty.
/// </summary>
public interface ISpreadsheetWriter
{
    Task WriteAsync(
        Stream output,
        string sheetName,
        IReadOnlyList<SpreadsheetColumn> columns,
        IAsyncEnumerable<object?[]> rows,
        CancellationToken ct);
}
