using System.Data.Common;

namespace ImpiFact.Infrastructure.Documents;

/// <summary>
/// SQL del consecutivo propio de cada motor (Modelo de datos detallado, sección 5): un solo UPDATE atómico sobre
/// <c>numbering_sequences</c> que además devuelve el valor. Cada proyecto <c>ImpiFact.Infrastructure.&lt;Motor&gt;</c>
/// trae su implementación y la registra con su <c>IDatabaseProvider</c>.
/// </summary>
/// <remarks>
/// Es la excepción documentada a la regla «Escrituras (16-nov)» (Arquitectura detallada, sección 16), que prohíbe
/// escribir filas auditadas sin <c>SaveChanges</c>. Para que la fila quede igual que si la escribiera EF Core, cada
/// sentencia sube <c>row_version</c> en uno y pone <c>updated_at</c> con el instante recibido, y filtra por
/// <c>company_id</c>; quien la llama revisa antes que la solicitud pueda escribir en esa empresa. Como sube
/// <c>row_version</c>, una copia de la fila cargada antes en EF Core queda vieja y su guardado falla por concurrencia
/// en lugar de pisar el número.
/// </remarks>
public interface INumberingSql
{
    /// <summary>Avanza el consecutivo en uno y devuelve el número tomado (el valor anterior de <c>next_number</c>).</summary>
    /// <returns>El número tomado, o nulo si no existe el consecutivo de esa empresa y clave.</returns>
    Task<long?> TakeNextAsync(
        DbConnection connection, DbTransaction transaction, Guid companyId, string scopeKey, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Deja <c>next_number = max(next_number, number + 1)</c> y devuelve el nuevo valor. Aunque el valor no cambie, la
    /// fila queda bloqueada hasta el final de la transacción.
    /// </summary>
    /// <returns>El nuevo <c>next_number</c>, o nulo si no existe el consecutivo de esa empresa y clave.</returns>
    Task<long?> RaiseAboveAsync(
        DbConnection connection, DbTransaction transaction, Guid companyId, string scopeKey, long number, DateTimeOffset now,
        CancellationToken ct);
}
