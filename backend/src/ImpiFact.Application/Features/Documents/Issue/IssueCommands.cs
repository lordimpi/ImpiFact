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

/// <summary>
/// <c>POST /api/v1/documents/invoices/aiu</c> (<c>/api/ubl2.1/invoice-aiu[/{testSetId}]</c>): AIU invoice (type 1,
/// operation 9; php-rules 10.1).
/// </summary>
public sealed record IssueAiuInvoiceCommand(IssueDocumentBody Body, byte[] RawBody, string? IdempotencyKey = null, string? TestSetId = null)
    : IIssueDocumentCommand;

/// <summary>
/// <c>POST /api/v1/documents/invoices/mandate</c> (<c>/api/ubl2.1/invoice-mandate[/{testSetId}]</c>): mandate invoice
/// (type 1, operation 11; php-rules 10.2).
/// </summary>
public sealed record IssueMandateInvoiceCommand(IssueDocumentBody Body, byte[] RawBody, string? IdempotencyKey = null, string? TestSetId = null)
    : IIssueDocumentCommand;

/// <summary>
/// <c>POST /api/v1/documents/invoices/transport</c> (<c>/api/ubl2.1/invoice-transport[/{testSetId}]</c>): freight
/// transport invoice (type 1, operation 21, code 12; php-rules 10.3).
/// </summary>
public sealed record IssueTransportInvoiceCommand(IssueDocumentBody Body, byte[] RawBody, string? IdempotencyKey = null, string? TestSetId = null)
    : IIssueDocumentCommand;

/// <summary>
/// <c>POST /api/v1/documents/invoices/export</c> (<c>/api/ubl2.1/invoice-export[/{testSetId}]</c>): export invoice (type
/// 2, template 02, operation 10; php-rules 11).
/// </summary>
public sealed record IssueExportInvoiceCommand(IssueDocumentBody Body, byte[] RawBody, string? IdempotencyKey = null, string? TestSetId = null)
    : IIssueDocumentCommand;

/// <summary>Tipo de documento que se emite.</summary>
public enum IssueKind
{
    Invoice,
    CreditNote,
    DebitNote,
}

/// <summary>
/// Invoice variants built by the invoice builder with per-variant options (php-rules, sections 9 to 11). AIU, mandate
/// and transport are type 1 invoices on template 01: same resolution, numbering, CUFE and plan quota as the standard
/// invoice. The export invoice is type 2 on template 02, with its own resolution and numbering.
/// </summary>
public enum InvoiceVariant
{
    Standard,

    /// <summary>Administration, contingencies and profit (operation 9, code 09).</summary>
    Aiu,

    /// <summary>Sale on behalf of third parties (operation 11, code 11).</summary>
    Mandate,

    /// <summary>Freight transport with RNDC data (operation 21, code 12).</summary>
    Transport,

    /// <summary>Export invoice (type 2, operation 10, code 10) with Incoterms and, optionally, the supplement K.</summary>
    Export,
}

/// <summary>Lo que cambia entre la factura, sus variantes y las notas (php-rules, secciones 3, 4, 9 y 10).</summary>
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
    TotalsDocumentKind TotalsKind,
    InvoiceVariant Variant = InvoiceVariant.Standard)
{
    public static readonly IssueSpec Invoice = new(
        IssueKind.Invoice, 1, "invoice_lines", "legal_monetary_totals", 10, null, null, null, "CUFE-SHA384", TotalsDocumentKind.Invoice);

    /// <summary>
    /// The operation of each variant is fixed: <c>type_operation_id</c> may be omitted or carry that same operation;
    /// another one is a 422 (php-rules 9.2, item 7: the PHP answered 500 to any value).
    /// </summary>
    public static readonly IssueSpec AiuInvoice = Invoice with
    {
        Variant = InvoiceVariant.Aiu, DefaultOperationTypeId = 9, AllowedOperationTypeIds = [9],
    };

    public static readonly IssueSpec MandateInvoice = Invoice with
    {
        Variant = InvoiceVariant.Mandate, DefaultOperationTypeId = 11, AllowedOperationTypeIds = [11],
    };

    public static readonly IssueSpec TransportInvoice = Invoice with
    {
        Variant = InvoiceVariant.Transport, DefaultOperationTypeId = 21, AllowedOperationTypeIds = [21],
    };

    /// <summary>Type 2 with its own resolution (php-rules 11, item 1) and the standard operation 10 (item 6).</summary>
    public static readonly IssueSpec ExportInvoice = Invoice with
    {
        DocumentTypeId = 2, Variant = InvoiceVariant.Export, DefaultOperationTypeId = 10, AllowedOperationTypeIds = [10],
    };

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
