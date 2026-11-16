using System.Text.Json;
using System.Text.Json.Serialization;
using ImpiFact.Domain.Catalog;

namespace ImpiFact.Infrastructure.Persistence.Seeding;

/// <summary>
/// Lee la semilla de un catálogo DIAN desde los JSON de <c>Seed/</c>, que viajan embebidos en el ensamblado
/// (Modelo de datos detallado, sección 9). <see cref="CatalogSynchronizer"/> las lleva a la base después de las
/// migraciones; no se usan con <c>HasData</c> porque EF copiaría las ~3.000 filas en la instantánea del modelo y en
/// cada migración nueva.
/// </summary>
/// <remarks>
/// Una columna del JSON que la entidad no conoce hace fallar la carga (<see cref="JsonUnmappedMemberHandling.Disallow"/>),
/// y una columna obligatoria que falta, también: así un JSON regenerado con otra forma no se siembra a medias.
/// </remarks>
internal static class CatalogSeed
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Nombre del recurso embebido de una tabla; lo fija el <c>LogicalName</c> del .csproj.</summary>
    public static string ResourceName(string table) => $"Seed/{table}.json";

    public static IReadOnlyList<T> Load<T>(string table)
        where T : CatalogEntry
    {
        using var stream = typeof(CatalogSeed).Assembly.GetManifestResourceStream(ResourceName(table))
            ?? throw new InvalidOperationException($"The seed resource '{ResourceName(table)}' is not embedded.");
        return JsonSerializer.Deserialize<List<T>>(stream, Options)
            ?? throw new InvalidOperationException($"The seed resource '{ResourceName(table)}' is empty.");
    }
}
