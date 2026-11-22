using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Abstractions.Documents;

/// <summary>
/// RADIAN persistence (phase 7a, T6): the invoices the company received from its sellers (<c>received_documents</c>), the
/// events sent on them or on the company's own invoices (<c>document_events</c>) and their files. Every query puts the
/// company in the WHERE, so a row of another company answers like a missing one (php-rules 7, rows 13 and 17). Rows are
/// added to the request's unit of work; the caller saves.
/// </summary>
public interface IRadianStore
{
    /// <summary>A received document of the company, tracked for changes; null if it does not exist in that company.</summary>
    Task<ReceivedDocument?> FindReceivedAsync(Guid companyId, Guid id, CancellationToken ct);

    /// <summary>The received document of the company with that CUFE (unique per company); null if there is none.</summary>
    Task<ReceivedDocument?> FindReceivedByCufeAsync(Guid companyId, string cufe, CancellationToken ct);

    /// <summary>A page of the company's received documents, newest issue date first.</summary>
    Task<IReadOnlyList<ReceivedDocument>> ListReceivedAsync(ReceivedDocumentFilter filter, CancellationToken ct);

    /// <summary>How many received documents match the filter, without paging.</summary>
    Task<long> CountReceivedAsync(ReceivedDocumentFilter filter, CancellationToken ct);

    void AddReceived(ReceivedDocument document);

    /// <summary>The events of a received document of the company, tracked, oldest first.</summary>
    Task<IReadOnlyList<DocumentEvent>> EventsOfReceivedAsync(Guid companyId, Guid receivedDocumentId, CancellationToken ct);

    /// <summary>The events of the company's received documents in <paramref name="receivedDocumentIds"/>, without tracking.</summary>
    Task<IReadOnlyList<DocumentEvent>> EventsOfReceivedAsync(Guid companyId, IReadOnlyCollection<Guid> receivedDocumentIds, CancellationToken ct);

    /// <summary>The events of an issued document of the company, tracked, oldest first.</summary>
    Task<IReadOnlyList<DocumentEvent>> EventsOfDocumentAsync(Guid companyId, Guid documentId, CancellationToken ct);

    void AddEvent(DocumentEvent documentEvent);

    /// <summary>
    /// Stores a file of the company under a key generated here (<c>{company}/radian/{owner}/{new id}{extension}</c>), never one
    /// that comes from a request, and returns the key.
    /// </summary>
    Task<string> PutFileAsync(Guid companyId, Guid ownerId, byte[] content, string contentType, string extension, CancellationToken ct);

    /// <summary>The bytes stored under <paramref name="key"/> if the key belongs to the company; null otherwise.</summary>
    Task<byte[]?> ReadFileAsync(Guid companyId, string key, CancellationToken ct);
}

/// <summary>Filter of the received documents of a company.</summary>
/// <param name="SupplierNit">Only documents of this seller (exact NIT).</param>
/// <param name="IssuedFrom">First issue day, inclusive.</param>
/// <param name="IssuedUntil">Last issue day, inclusive.</param>
public sealed record ReceivedDocumentFilter(
    Guid CompanyId, string? SupplierNit, DateOnly? IssuedFrom, DateOnly? IssuedUntil, int Skip, int Take);
