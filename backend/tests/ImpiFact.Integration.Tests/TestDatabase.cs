using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Postgres;

namespace ImpiFact.Integration.Tests;

/// <summary>
/// Motor de base de datos de las pruebas, elegido por configuración como en la API, el Worker y el Migrator: los hosts de
/// prueba registran todos los motores disponibles (<see cref="Providers"/>) y escriben <see cref="ProviderName"/> en
/// <c>Database:Provider</c>. Las pruebas que levantan la API leen la misma variable de entorno por su configuración.
/// </summary>
/// <remarks>
/// Para correr las pruebas con otro motor se define la variable de entorno <c>Database__Provider</c> (por ejemplo
/// <c>SqlServer</c>). Sin ella se usa PostgreSQL. Los contenedores de cada prueba todavía son de PostgreSQL: cada
/// motor nuevo agrega aquí su proveedor y su contenedor.
/// </remarks>
public static class TestDatabase
{
    /// <summary>Variable de entorno con el motor; es la misma clave <c>Database:Provider</c> de los hosts.</summary>
    public const string ProviderVariable = "Database__Provider";

    private const string DefaultProvider = "Postgres";

    /// <summary>Todos los motores con proyecto en la solución, igual que en los hosts.</summary>
    public static IDatabaseProvider[] Providers => [new PostgresDatabaseProvider()];

    /// <summary>Nombre del motor con que corren las pruebas.</summary>
    public static string ProviderName =>
        Environment.GetEnvironmentVariable(ProviderVariable) is { Length: > 0 } name ? name.Trim() : DefaultProvider;
}
