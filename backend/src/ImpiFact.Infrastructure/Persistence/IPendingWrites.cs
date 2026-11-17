using System.Data.Common;

namespace ImpiFact.Infrastructure.Persistence;

/// <summary>
/// Escrituras atómicas que un adaptador deja pendientes para el próximo guardado, porque EF Core (Entity Framework) no
/// sabe expresarlas: por ejemplo, <c>used = used + 1</c> en <c>plan_usage</c>. La unidad de trabajo
/// (<see cref="EfUnitOfWork"/>) las ejecuta dentro de la misma transacción del guardado, después de los cambios de EF:
/// si esos cambios fallan (por ejemplo, por concurrencia), las escrituras no se ejecutan; si se revierte la
/// transacción, se revierten con ella.
/// </summary>
/// <remarks>
/// Son la misma excepción documentada que el UPDATE del consecutivo (<c>INumberingSql</c>) a la regla «Escrituras
/// (16-nov)» de la Arquitectura detallada, sección 16: quien las agrega revisa antes que la solicitud pueda escribir en
/// esa empresa.
/// </remarks>
public interface IPendingWrites
{
    bool HasPending { get; }

    /// <summary>Ejecuta las escrituras pendientes en la conexión y la transacción del guardado, y las olvida.</summary>
    Task FlushAsync(DbConnection connection, DbTransaction transaction, CancellationToken ct);

    /// <summary>Olvida las escrituras pendientes sin ejecutarlas (la transacción se revirtió).</summary>
    void Discard();
}
