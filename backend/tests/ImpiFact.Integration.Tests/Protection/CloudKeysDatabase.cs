using ImpiFact.Integration.Tests.Protection.DataProtection;

namespace ImpiFact.Integration.Tests.Protection;

/// <summary>
/// Crea, en el servidor de base de datos de una prueba de un proveedor de nube, una base nueva con la tabla
/// <c>data_protection_keys</c> tal como la mapea el contexto de producción (igual que <see cref="KeysDatabase"/>).
/// </summary>
public static class CloudKeysDatabase
{
    public static async Task<string> CreateAsync(TestDatabaseServer server)
    {
        var name = "keys_" + Guid.NewGuid().ToString("N");
        await server.CreateDatabaseAsync(name);

        var connectionString = server.ConnectionStringFor(name);
        await using var context = new KeysOnlyDbContext(TestDatabase.ContextOptions<KeysOnlyDbContext>(connectionString));
        await context.Database.EnsureCreatedAsync();
        return connectionString;
    }
}
