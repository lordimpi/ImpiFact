using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Features.Documents.Totals;

namespace ImpiFact.Application.Features.Documents.Issue;

/// <summary>Datos comunes de una emisión: el cuerpo leído, sus bytes tal como llegaron y los encabezados que la afectan.</summary>
public interface IIssueDocumentCommand : ICommand
{
    IssueDocumentBody Body { get; }

    /// <summary>Cuerpo JSON tal como llegó; se guarda (sin secretos) y su resumen identifica la solicitud.</summary>
    byte[] RawBody { get; }

    /// <summary>Encabezado <c>Idempotency-Key</c>; nulo si no vino.</summary>
    string? IdempotencyKey { get; }

    /// <summary>Set de pruebas de la ruta <c>/{testSetId}</c>; nulo en la ruta normal.</summary>
    string? TestSetId { get; }
}

/// <summary><c>POST /api/ubl2.1/invoice[/{testSetId}]</c>: factura electrónica de venta nacional (tipo 1).</summary>
public sealed record IssueInvoiceCommand(IssueDocumentBody Body, byte[] RawBody, string? IdempotencyKey = null, string? TestSetId = null)
    : IIssueDocumentCommand;

/// <summary><c>POST /api/ubl2.1/credit-note[/{testSetId}]</c>: nota crédito (tipo 4).</summary>
public sealed record IssueCreditNoteCommand(IssueDocumentBody Body, byte[] RawBody, string? IdempotencyKey = null, string? TestSetId = null)
    : IIssueDocumentCommand;

/// <summary><c>POST /api/ubl2.1/debit-note[/{testSetId}]</c>: nota débito (tipo 5).</summary>
public sealed record IssueDebitNoteCommand(IssueDocumentBody Body, byte[] RawBody, string? IdempotencyKey = null, string? TestSetId = null)
    : IIssueDocumentCommand;

/// <summary>Tipo de documento que se emite.</summary>
public enum IssueKind
{
    Invoice,
    CreditNote,
    DebitNote,
}

/// <summary>Lo que cambia entre la factura y las notas (php-rules, secciones 3 y 4).</summary>
internal sealed record IssueSpec(
    IssueKind Kind,
    int DocumentTypeId,
    string LinesField,
    string TotalsField,
    int DefaultOperationTypeId,
    int[]? AllowedOperationTypeIds,
    int? OperationWithoutReferenceId,
    int? MaxDiscrepancyCode,
    string UuidAlgorithm,
    TotalsDocumentKind TotalsKind)
{
    public static readonly IssueSpec Invoice = new(
        IssueKind.Invoice, 1, "invoice_lines", "legal_monetary_totals", 10, null, null, null, "CUFE-SHA384", TotalsDocumentKind.Invoice);

    /// <summary>Operaciones 7, 8, 12, 30, 31, 32; por defecto 12 (código 20); sin referencia, la 8 (código 22).</summary>
    public static readonly IssueSpec CreditNote = new(
        IssueKind.CreditNote, 4, "credit_note_lines", "legal_monetary_totals", 12, [7, 8, 12, 30, 31, 32], 8, 6, "CUDE-SHA384",
        TotalsDocumentKind.CreditNote);

    /// <summary>Operaciones 4, 5, 6, 30, 31, 32; por defecto 6 (código 30); sin referencia, la 5 (código 32).</summary>
    public static readonly IssueSpec DebitNote = new(
        IssueKind.DebitNote, 5, "debit_note_lines", "requested_monetary_totals", 6, [4, 5, 6, 30, 31, 32], 5, 4, "CUDE-SHA384",
        TotalsDocumentKind.DebitNote);

    public bool IsNote => Kind != IssueKind.Invoice;

    public List<LineBody>? LinesOf(IssueDocumentBody body) => Kind switch
    {
        IssueKind.Invoice => body.InvoiceLines,
        IssueKind.CreditNote => body.CreditNoteLines,
        _ => body.DebitNoteLines,
    };

    public MonetaryTotalsBody? TotalsOf(IssueDocumentBody body) =>
        Kind == IssueKind.DebitNote ? body.RequestedMonetaryTotals : body.LegalMonetaryTotals;
}
