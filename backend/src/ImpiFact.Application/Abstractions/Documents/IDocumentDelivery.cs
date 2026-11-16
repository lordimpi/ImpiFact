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
}
