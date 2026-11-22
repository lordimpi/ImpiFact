using System.Globalization;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Features.Documents.Queries.ExportDocuments;

/// <summary>One export column: header and layout (shared by every format) plus the typed value of a row.</summary>
public sealed record DocumentExportColumn(SpreadsheetColumn Layout, Func<DocumentSummary, object> Value)
{
    public string Header => Layout.Header;

    public SpreadsheetCellKind Kind => Layout.Kind;
}

/// <summary>
/// The columns of the document export, in order. The CSV (<see cref="DocumentCsv"/>) and the Excel workbook read the
/// same definitions, so both files always carry the same headers and values. Labels are in Spanish, as the panel.
/// </summary>
public static class DocumentExportColumns
{
    private const string CufeHeader = "CUFE/CUDE";

    public static IReadOnlyList<DocumentExportColumn> All { get; } =
    [
        Column("Tipo", SpreadsheetCellKind.Text, 14, row => KindLabel(row.DocumentTypeId)),
        Column("Número", SpreadsheetCellKind.Text, 16, row => row.FullNumber),
        Column("Fecha de emisión", SpreadsheetCellKind.Date, 16, row => row.IssueDate),
        Column("Hora de emisión", SpreadsheetCellKind.Time, 15, row => row.IssueTime),
        Column("Identificación del cliente", SpreadsheetCellKind.Text, 24, row => row.Customer.IdentificationNumber),
        Column("Cliente", SpreadsheetCellKind.Text, 40, row => row.Customer.Name),
        Column("Subtotal", SpreadsheetCellKind.Amount, 16, row => row.Totals.LineExtensionAmount),
        Column("Impuestos", SpreadsheetCellKind.Amount, 16, row => row.Totals.TaxInclusiveAmount - row.Totals.TaxExclusiveAmount),
        Column("Total", SpreadsheetCellKind.Amount, 16, row => row.Totals.PayableAmount),
        Column("Estado", SpreadsheetCellKind.Text, 26, row => StatusLabel(row.Status)),
        Column("Estado del correo", SpreadsheetCellKind.Text, 18, row => EmailLabel(row.EmailStatus)),
        Column(CufeHeader, SpreadsheetCellKind.Text, 40, row => row.Cufe ?? ""),
    ];

    /// <summary>
    /// The columns of the PDF report: every column except CUFE/CUDE, whose 96 characters do not fit a landscape A4 page
    /// next to the others. The CSV and the Excel workbook keep every column.
    /// </summary>
    public static IReadOnlyList<DocumentExportColumn> Report { get; } = [.. All.Where(column => column.Header != CufeHeader)];

    /// <summary>Header and layout of every column, for the spreadsheet writer.</summary>
    public static IReadOnlyList<SpreadsheetColumn> Layouts { get; } = [.. All.Select(column => column.Layout)];

    /// <summary>The typed values of a row, one per column.</summary>
    public static object?[] Values(DocumentSummary row)
    {
        var values = new object?[All.Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = All[i].Value(row);
        }

        return values;
    }

    public static string StatusLabel(DocumentStatus status) => status switch
    {
        DocumentStatus.Received => "Recibido",
        DocumentStatus.Queued => "En cola",
        DocumentStatus.Sent => "Enviado a la DIAN",
        DocumentStatus.Accepted => "Aceptado",
        DocumentStatus.AcceptedWithWarnings => "Aceptado con advertencias",
        DocumentStatus.Rejected => "Rechazado",
        DocumentStatus.Unconfirmed => "Sin confirmar",
        DocumentStatus.Failed => "Fallido",
        DocumentStatus.ContingencyPending => "Contingencia pendiente",
        DocumentStatus.Draft => "Borrador",
        _ => status.ToString(),
    };

    private static DocumentExportColumn Column(string header, SpreadsheetCellKind kind, double width, Func<DocumentSummary, object> value) =>
        new(new SpreadsheetColumn(header, kind, width), value);

    private static string EmailLabel(EmailStatus status) => status switch
    {
        EmailStatus.Sent => "Enviado",
        EmailStatus.Failed => "Falló",
        _ => "No enviado",
    };

    /// <summary>Spanish name of a document type, as the «Tipo» column shows it.</summary>
    public static string KindLabel(int documentTypeId) => (documentTypeId, DocumentKinds.NameOf(documentTypeId)) switch
    {
        (_, DocumentKinds.Invoice) => "Factura",
        (_, DocumentKinds.CreditNote) => "Nota crédito",
        (_, DocumentKinds.DebitNote) => "Nota débito",
        (11, _) => "Documento soporte",
        (13, _) => "Nota de ajuste al documento soporte",
        _ => string.Create(CultureInfo.InvariantCulture, $"Tipo {documentTypeId}"),
    };
}
