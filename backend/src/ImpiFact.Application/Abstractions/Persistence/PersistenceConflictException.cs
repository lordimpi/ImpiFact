namespace ImpiFact.Application.Abstractions.Persistence;

/// <summary>
/// Otra solicitud cambió los mismos datos al mismo tiempo: la fila ya no tiene la versión que se leyó
/// (concurrencia optimista) o el dato choca con un índice único que otra solicitud acaba de ocupar. La lanza
/// <see cref="IUnitOfWork.SaveChangesAsync"/>; el decorador de transacción la convierte en un error de conflicto
/// (409) en lugar de un error interno, porque repetir la solicitud suele resolverlo.
/// </summary>
public sealed class PersistenceConflictException(string message, Exception innerException) : Exception(message, innerException);
