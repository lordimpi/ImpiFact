using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Abstractions.Documents;

/// <summary>
/// Lo que el envío a la DIAN (la autoridad tributaria de Colombia) necesita de la base: el documento con rastreo de
/// cambios, para moverlo de estado, y sus intentos contra el servicio web (<c>dian_submissions</c>). Los cambios se
/// guardan con <see cref="Persistence.IUnitOfWork"/>; el Worker no pasa por el decorador de transacción.
/// </summary>
/// <remarks>Como los demás puertos con <c>companyId</c>, busca solo en esa empresa.</remarks>
public interface IDocumentDelivery
{
    /// <summary>El documento con rastreo de cambios; nulo si no existe en la empresa.</summary>
    Task<Document?> FindForUpdateAsync(Guid companyId, Guid documentId, CancellationToken ct);

    /// <summary>Número del próximo intento del documento: uno más que el mayor guardado o agregado; 1 si no hay.</summary>
    Task<int> NextSubmissionAttemptAsync(Guid companyId, Guid documentId, CancellationToken ct);

    /// <summary>Agrega el intento; se guarda con el próximo <c>SaveChangesAsync</c>.</summary>
    /// <exception cref="InvalidOperationException">Si la solicitud es de otra empresa.</exception>
    void AddSubmission(DianSubmission submission);

    /// <summary>
    /// Inicio (<c>started_at</c>) del primer intento guardado que pudo llegar a la DIAN; nulo si ninguno pudo. Se usa
    /// solo con un documento que sigue en <c>Queued</c>, cuyas filas guardadas pudo dejar un intento que no alcanzó a
    /// cambiar su estado.
    /// </summary>
    /// <remarks>
    /// Un intento pudo llegar si su resultado no quedó guardado (<c>finished_at</c> nulo: la llamada empezó y el
    /// proceso se cayó o falló al guardar lo que respondió la DIAN), si la DIAN respondió un resultado
    /// (<c>is_valid</c> no nulo, por ejemplo la regla 90) o si es una consulta de estado. Los intentos que la DIAN no
    /// recibió se guardan con <c>finished_at</c> y sin resultado; cualquier otro desenlace cambia el estado del
    /// documento en el mismo guardado.
    /// </remarks>
    /// <param name="exceptSubmissionId">Intento que no se cuenta (el que está en curso).</param>
    Task<DateTimeOffset?> FirstPossibleArrivalAsync(Guid companyId, Guid documentId, Guid? exceptSubmissionId, CancellationToken ct);

    /// <summary>
    /// Textos guardados de los envíos del documento (SendBillSync y SendTestSetAsync) que la DIAN respondió como no
    /// válidos (<c>is_valid</c> falso): sus mensajes de error y su descripción, en el orden de los intentos. Vacía si
    /// no hay. Sirve para saber si un envío recibió la regla 90.
    /// </summary>
    Task<IReadOnlyList<string>> InvalidSendMessagesAsync(Guid companyId, Guid documentId, CancellationToken ct);

    /// <summary>
    /// The company's invoices type 04 (type 12) still in <see cref="DocumentStatus.ContingencyPending"/>, without tracking,
    /// ordered by prefix and number: all of them, those of <paramref name="prefix"/>, or the one with that prefix and
    /// <paramref name="number"/>. Only that company's documents, never another company's.
    /// </summary>
    Task<IReadOnlyList<Document>> ListContingencyPendingAsync(Guid companyId, string? prefix, long? number, CancellationToken ct);
}
