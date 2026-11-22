using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Abstractions.Documents;

/// <summary>
/// Batches of documents (<c>document_batches</c>; phase 7a, T8) and their files. Every query puts the company in the WHERE,
/// so a batch of another company answers like a missing one. Rows are added to the unit of work of the request or of the
/// Worker message; the caller saves. The batch carries a row version: two changes of the same batch at the same time (two
/// sends, or a send and an addition) cannot both be saved.
/// </summary>
public interface IDocumentBatches
{
    /// <summary>The company's batch, tracked for changes; null if it does not exist in that company.</summary>
    Task<DocumentBatch?> FindAsync(Guid companyId, Guid id, CancellationToken ct);

    /// <summary>The company's batch with that name (unique per company), tracked; null if there is none.</summary>
    Task<DocumentBatch?> FindByNameAsync(Guid companyId, string name, CancellationToken ct);

    void Add(DocumentBatch batch);

    /// <summary>A page of the company's batches, newest first, without tracking.</summary>
    Task<IReadOnlyList<DocumentBatch>> ListAsync(Guid companyId, int skip, int take, CancellationToken ct);

    Task<long> CountAsync(Guid companyId, CancellationToken ct);

    /// <summary>The documents of the company's batch, tracked, in the order they were added.</summary>
    Task<IReadOnlyList<Document>> DocumentsAsync(Guid companyId, Guid batchId, CancellationToken ct);

    /// <summary>
    /// The DIAN messages of each document of the batch: those of its last answered attempt with messages, by document id.
    /// Documents without messages are missing from the dictionary.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> LastMessagesAsync(Guid companyId, IReadOnlyCollection<Guid> documentIds, CancellationToken ct);

    /// <summary>
    /// Stores a file of the batch under a key generated here (<c>{company}/batches/{batch}/{new id}{extension}</c>), never one
    /// that comes from a request, and returns the key.
    /// </summary>
    Task<string> PutFileAsync(Guid companyId, Guid batchId, byte[] content, string contentType, string extension, CancellationToken ct);
}
