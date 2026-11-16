using System.Data.Common;

namespace ImpiFact.Application.Abstractions.Persistence;

/// <summary>
/// Abre conexiones para las consultas de lectura (Arquitectura detallada, sección 4.4): las escrituras van con
/// EF Core (Entity Framework, el mapeador de objetos a tablas) y las lecturas con Dapper, una librería que
/// ejecuta SQL y llena objetos. Los filtros globales de EF no aplican a estas consultas: todo SQL de lectura
/// lleva <c>company_id</c> en el WHERE (Modelo de datos, sección 6).
/// </summary>
public interface IReadConnectionFactory
{
    /// <summary>Devuelve una conexión ya abierta; quien la pide la libera al terminar.</summary>
    Task<DbConnection> OpenAsync(CancellationToken ct);
}
