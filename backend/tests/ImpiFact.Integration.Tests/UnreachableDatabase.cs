namespace ImpiFact.Integration.Tests;

/// <summary>
/// Cadena de conexión para las pruebas que levantan la API sin base de datos. Apunta a un puerto donde nunca hay
/// un servidor (el 1 de la propia máquina), así una prueba nunca escribe por accidente en un PostgreSQL o MySQL instalado
/// en el equipo de quien la corre, por ejemplo el anillo de llaves de Data Protection.
/// </summary>
internal static class UnreachableDatabase
{
    /// <summary>La cadena con las claves del controlador del motor de las pruebas (MySqlConnector no acepta «Timeout»).</summary>
    public static string ConnectionString => TestDatabase.IsMySql
        ? "Server=127.0.0.1;Port=1;Database=impifact_unreachable;Connection Timeout=1"
        : "Host=127.0.0.1;Port=1;Database=impifact_unreachable;Timeout=1";
}
