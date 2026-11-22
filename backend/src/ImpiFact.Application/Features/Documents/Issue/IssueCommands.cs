using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Features.Documents.Totals;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;

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

    /// <summary>
    /// Batch the document is issued into (phase 7a, T8; php-rules 15.1): it is validated, numbered and signed like any other,
    /// but waits in the batch, without a DIAN call, until the batch is sent with SendBillAsync. Null for a document sent alone.
    /// </summary>
    Guid? BatchId { get; }
}

/// <summary><c>POST /api/v1/documents/invoices[/test-set/{testSetId}]</c>: factura electrónica de venta nacional (tipo 1).</summary>
public sealed record IssueInvoiceCommand(IssueDocumentBody Body, byte[] RawBody, string? IdempotencyKey = null, string? TestSetId = null)
    : IIssueDocumentCommand
{
    public Guid? BatchId { get; init; }
}

/// <summary><c>POST /api/v1/documents/credit-notes[/test-set/{testSetId}]</c>: nota crédito (tipo 4).</summary>
public sealed record IssueCreditNoteCommand(IssueDocumentBody Body, byte[] RawBody, string? IdempotencyKey = null, string? TestSetId = null)
    : IIssueDocumentCommand
{
    public Guid? BatchId { get; init; }
}

/// <summary><c>POST /api/v1/documents/debit-notes[/test-set/{testSetId}]</c>: nota débito (tipo 5).</summary>
public sealed record IssueDebitNoteCommand(IssueDocumentBody Body, byte[] RawBody, string? IdempotencyKey = null, string? TestSetId = null)
    : IIssueDocumentCommand
{
    public Guid? BatchId { get; init; }
}

/// <summary>
/// <c>POST /api/v1/documents/invoices/aiu</c> (the PHP's <c>invoice-aiu[/{testSetId}]</c>): AIU invoice (type 1,
/// operation 9; php-rules 10.1).
/// </summary>
public sealed record IssueAiuInvoiceCommand(IssueDocumentBody Body, byte[] RawBody, string? IdempotencyKey = null, string? TestSetId = null)
    : IIssueDocumentCommand
{
    public Guid? BatchId { get; init; }
}

/// <summary>
/// <c>POST /api/v1/documents/invoices/mandate</c> (the PHP's <c>invoice-mandate[/{testSetId}]</c>): mandate invoice
/// (type 1, operation 11; php-rules 10.2).
/// </summary>
public sealed record IssueMandateInvoiceCommand(IssueDocumentBody Body, byte[] RawBody, string? IdempotencyKey = null, string? TestSetId = null)
    : IIssueDocumentCommand
{
    public Guid? BatchId { get; init; }
}

/// <summary>
/// <c>POST /api/v1/documents/invoices/transport</c> (the PHP's <c>invoice-transport[/{testSetId}]</c>): freight
/// transport invoice (type 1, operation 21, code 12; php-rules 10.3).
/// </summary>
public sealed record IssueTransportInvoiceCommand(IssueDocumentBody Body, byte[] RawBody, string? IdempotencyKey = null, string? TestSetId = null)
    : IIssueDocumentCommand
{
    /// <summary>The PHP has no batch for the transport invoice (php-rules 15.1, item 1).</summary>
    public Guid? BatchId => null;
}

/// <summary>
/// <c>POST /api/v1/documents/invoices/export</c> (the PHP's <c>invoice-export[/{testSetId}]</c>): export invoice (type
/// 2, template 02, operation 10; php-rules 11).
/// </summary>
public sealed record IssueExportInvoiceCommand(IssueDocumentBody Body, byte[] RawBody, string? IdempotencyKey = null, string? TestSetId = null)
    : IIssueDocumentCommand
{
    public Guid? BatchId { get; init; }
}

/// <summary>
/// <c>POST /api/v1/documents/invoices/contingency</c> (the PHP's <c>invoice-contingency[/{testSetId}]</c>): contingency
/// invoice type 03, the transcription of a paper invoice (type 3, template 03, CUDE; php-rules 12.1).
/// </summary>
public sealed record IssueContingencyInvoiceCommand(IssueDocumentBody Body, byte[] RawBody, string? IdempotencyKey = null, string? TestSetId = null)
    : IIssueDocumentCommand
{
    public Guid? BatchId { get; init; }
}

/// <summary>
/// <c>POST /api/v1/documents/invoices/dian-contingency</c> (the PHP's <c>invoice-contingency-4</c>): invoice type 04,
/// issued while the DIAN cannot validate (type 12, template 04; php-rules 12.2). It is stored signed and pending; the
/// Worker sends it later. There is no test-set route, as in the PHP.
/// </summary>
public sealed record IssueDianContingencyInvoiceCommand(IssueDocumentBody Body, byte[] RawBody, string? IdempotencyKey = null)
    : IIssueDocumentCommand
{
    public string? TestSetId => null;

    /// <summary>The invoice type 04 is sent on its own when the DIAN answers again; the PHP has no batch for it.</summary>
    public Guid? BatchId => null;
}

/// <summary>
/// <c>POST /api/v1/documents/support-documents</c> (the PHP's <c>support-document[/{testSetId}]</c>): support document in
/// purchases from sellers not obliged to invoice (type 11, template 05, CUDS; php-rules 13).
/// </summary>
public sealed record IssueSupportDocumentCommand(IssueDocumentBody Body, byte[] RawBody, string? IdempotencyKey = null, string? TestSetId = null)
    : IIssueDocumentCommand
{
    /// <summary>The support document has its own software and environment, and the PHP has no batch for it (php-rules 15.1, item 1).</summary>
    public Guid? BatchId => null;
}

/// <summary>
/// <c>POST /api/v1/documents/support-documents/adjustment-notes</c> (the PHP's <c>sd-credit-note[/{testSetId}]</c>):
/// adjustment note to a support document (type 13, template 95, CUDS; php-rules 14).
/// </summary>
public sealed record IssueSupportDocumentAdjustmentNoteCommand(
    IssueDocumentBody Body, byte[] RawBody, string? IdempotencyKey = null, string? TestSetId = null) : IIssueDocumentCommand
{
    public Guid? BatchId => null;
}

/// <summary>Tipo de documento que se emite.</summary>
public enum IssueKind
{
    Invoice,
    CreditNote,
    DebitNote,

    /// <summary>Support document (type 11): the company buys from a seller not obliged to invoice.</summary>
    SupportDocument,

    /// <summary>Adjustment note to a support document (type 13, CreditNoteTypeCode 95).</summary>
    SupportDocumentAdjustmentNote,
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

    /// <summary>
    /// Contingency invoice type 03 (type 3, template 03): the transcription of a paper invoice, with its own type 3
    /// resolution and the CUDE with the software PIN (annex 1.9, 11.1.4, Nota-2).
    /// </summary>
    Contingency,

    /// <summary>
    /// Invoice type 04 (type 12, template 04): the sales invoice numbering and CUFE (annex 1.9, 12.2), stored pending until
    /// the DIAN answers again.
    /// </summary>
    DianContingency,
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

    /// <summary>
    /// Type 3 with its own contingency resolution (php-rules 12.1, item 1), the fixed operation 10 (item 4) and the CUDE
    /// with the software PIN (item 3; annex 1.9, 11.1.4, Nota-2).
    /// </summary>
    public static readonly IssueSpec ContingencyInvoice = Invoice with
    {
        DocumentTypeId = DocumentFamilies.ContingencyInvoiceType, Variant = InvoiceVariant.Contingency, DefaultOperationTypeId = 10,
        AllowedOperationTypeIds = [10], UuidAlgorithm = "CUDE-SHA384",
    };

    /// <summary>
    /// Type 12 (code 04) on the resolution and consecutive of the sales invoice (type 1): annex 1.9, section 12.2, «La
    /// numeración utilizada para este tipo de documento es la de Factura electrónica de venta», so its code is the CUFE
    /// with that technical key. The PHP used a type 12 resolution and computed the code with the PIN (php-rules 12.2, item
    /// 3). Fixed operation 10, like the PHP.
    /// </summary>
    public static readonly IssueSpec DianContingencyInvoice = Invoice with
    {
        DocumentTypeId = DocumentFamilies.DianContingencyInvoiceType, Variant = InvoiceVariant.DianContingency, DefaultOperationTypeId = 10,
        AllowedOperationTypeIds = [10],
    };

    /// <summary>Operaciones 7, 8, 12, 30, 31, 32; por defecto 12 (código 20); sin referencia, la 8 (código 22).</summary>
    public static readonly IssueSpec CreditNote = new(
        IssueKind.CreditNote, 4, "credit_note_lines", "legal_monetary_totals", 12, [7, 8, 12, 30, 31, 32], 8, 6, "CUDE-SHA384",
        TotalsDocumentKind.CreditNote);

    /// <summary>Operaciones 4, 5, 6, 30, 31, 32; por defecto 6 (código 30); sin referencia, la 5 (código 32).</summary>
    public static readonly IssueSpec DebitNote = new(
        IssueKind.DebitNote, 5, "debit_note_lines", "requested_monetary_totals", 6, [4, 5, 6, 30, 31, 32], 5, 4, "CUDE-SHA384",
        TotalsDocumentKind.DebitNote);

    /// <summary>
    /// Support document: its own type 11 resolution and numbering, the seller in <c>seller</c>, operations 23 (resident,
    /// default, code 10) or 24 (non-resident, code 11), the CUDS with the support-document software PIN (php-rules 13.1).
    /// </summary>
    public static readonly IssueSpec SupportDocument = new(
        IssueKind.SupportDocument, 11, "invoice_lines", "legal_monetary_totals", 23, [23, 24], null, null, "CUDS-SHA384",
        TotalsDocumentKind.Invoice);

    /// <summary>
    /// Adjustment note: <c>type_operation_id</c> may be 23 or 24, but without it the PHP uses 10 (code 10, php-rules 14,
    /// item 4); discrepancy codes 1 to 5 of <c>credit_note_discrepancy_responses_sd</c> (item 3: the PHP crashed on 6).
    /// </summary>
    public static readonly IssueSpec SupportDocumentAdjustmentNote = new(
        IssueKind.SupportDocumentAdjustmentNote, 13, "credit_note_lines", "legal_monetary_totals", 10, [23, 24], null, 5, "CUDS-SHA384",
        TotalsDocumentKind.CreditNote);

    public bool IsNote => Kind is IssueKind.CreditNote or IssueKind.DebitNote or IssueKind.SupportDocumentAdjustmentNote;

    /// <summary>Document type whose resolutions and consecutive the document uses: its own, except the invoice type 04.</summary>
    public int ResolutionTypeId => DocumentFamilies.ResolutionTypeOf(DocumentTypeId);

    /// <summary>
    /// Other document types that share this one's numbering, so a number used by one of them is taken: the sales invoice
    /// (type 1, with its variants) and the invoice type 04 (type 12) share the type 1 resolution (annex 1.9, 12.2).
    /// </summary>
    public IReadOnlyList<int> SharedNumberTypeIds => DocumentTypeId switch
    {
        1 => [DocumentFamilies.DianContingencyInvoiceType],
        DocumentFamilies.DianContingencyInvoiceType => [1],
        _ => [],
    };

    /// <summary>The CUFE needs the technical key of the resolution; the CUDE and the CUDS do not.</summary>
    public bool NeedsTechnicalKey => Kind == IssueKind.Invoice && UuidAlgorithm == "CUFE-SHA384";

    /// <summary>The support document and its adjustment note: the support-document family (software, environment, plan).</summary>
    public bool IsSupportDocumentFamily => Kind is IssueKind.SupportDocument or IssueKind.SupportDocumentAdjustmentNote;

    /// <summary>Family whose environment, software and plan quota the document uses.</summary>
    public DocumentKind Family => IsSupportDocumentFamily ? DocumentKind.SupportDocument : DocumentKind.Invoice;

    /// <summary>Documents written by <c>InvoiceUblBuilder</c> (an Invoice root): the invoices and the support document.</summary>
    public bool IsInvoiceDocument => Kind is IssueKind.Invoice or IssueKind.SupportDocument;

    /// <summary>
    /// The other party of the request: the buyer (<c>customer</c>) of the invoice and its notes, the seller not obliged
    /// to invoice (<c>seller</c>) of the support document and its adjustment note.
    /// </summary>
    public string PartyField => IsSupportDocumentFamily ? "seller" : "customer";

    public CustomerBody? PartyOf(IssueDocumentBody body) => IsSupportDocumentFamily ? body.Seller : body.Customer;

    public List<LineBody>? LinesOf(IssueDocumentBody body) => Kind switch
    {
        IssueKind.Invoice or IssueKind.SupportDocument => body.InvoiceLines,
        IssueKind.CreditNote or IssueKind.SupportDocumentAdjustmentNote => body.CreditNoteLines,
        _ => body.DebitNoteLines,
    };

    public MonetaryTotalsBody? TotalsOf(IssueDocumentBody body) =>
        Kind == IssueKind.DebitNote ? body.RequestedMonetaryTotals : body.LegalMonetaryTotals;

    /// <summary>
    /// The <c>health_fields</c> that make a national invoice a health-sector one (php-rules 17); null for every other
    /// document and variant, which ignore them like ApiDian Community.
    /// </summary>
    public HealthFieldsBody? HealthOf(IssueDocumentBody body) =>
        Kind == IssueKind.Invoice && Variant == InvoiceVariant.Standard ? body.HealthFields : null;
}
