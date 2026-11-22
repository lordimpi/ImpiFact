using ImpiFact.Domain.Common;

namespace ImpiFact.Domain.Documents;

/// <summary>
/// Family of a document type (<c>type_documents.id</c>): the one whose environment, software and plan quota it uses, like
/// <c>getResolutionEnvironmentId</c> of the PHP. The Documents module (issue, delivery to the DIAN, PDF, email) reads it
/// here; the Companies module cannot use Documents entities, so it keeps the same rule in <c>Environments.KindOf</c>.
/// </summary>
public static class DocumentFamilies
{
    /// <summary>Contingency invoice type 03 (type 3): the transcription of a paper invoice issued while the invoicer could not.</summary>
    public const int ContingencyInvoiceType = 3;

    /// <summary>
    /// Invoice type 04 (type 12): issued while the DIAN could not validate, stored as <see cref="DocumentStatus.ContingencyPending"/>
    /// and sent when the DIAN answers again.
    /// </summary>
    public const int DianContingencyInvoiceType = 12;

    /// <summary>Support document (type 11, code 05).</summary>
    public const int SupportDocumentType = 11;

    /// <summary>Adjustment note to a support document (type 13, code 95).</summary>
    public const int SupportDocumentAdjustmentNoteType = 13;

    /// <summary>
    /// Document type whose resolutions number this type: its own, except the invoice type 04, which keeps the numbering of
    /// the sales invoice (type 1; annex 1.9, section 12.2).
    /// </summary>
    public static int ResolutionTypeOf(int documentTypeId) => documentTypeId == DianContingencyInvoiceType ? 1 : documentTypeId;

    /// <summary>8 and 14 events, 9 and 10 payroll, 11 and 13 support document, 15 to 26 equivalent document, the rest invoice and notes.</summary>
    public static DocumentKind Of(int documentTypeId) => documentTypeId switch
    {
        8 or 14 => DocumentKind.Events,
        9 or 10 => DocumentKind.Payroll,
        SupportDocumentType or SupportDocumentAdjustmentNoteType => DocumentKind.SupportDocument,
        >= 15 and <= 26 => DocumentKind.EquivalentDocument,
        _ => DocumentKind.Invoice,
    };
}
