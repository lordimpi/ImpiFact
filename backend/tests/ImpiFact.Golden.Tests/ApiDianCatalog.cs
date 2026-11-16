using System.Text.Json;

namespace ImpiFact.Golden.Tests;

/// <summary>Una fila de un catálogo de ApiDian, reducida a las columnas que usa el XML.</summary>
public sealed record CatalogEntry(string Name, string Code, string? DepartmentId = null, string? CodeAgency = null);

/// <summary>
/// Catálogos de ApiDian (tablas que traducen un ID a su código y nombre DIAN) cargados desde catalogs.json.
/// Ese archivo de datos de prueba se extrajo de los CSV con los que ApiDian llena su base de datos inicial.
/// Las pruebas nunca leen la copia local del código de ApiDian.
/// </summary>
public sealed class ApiDianCatalog
{
    private readonly Dictionary<string, Dictionary<string, CatalogEntry>> _catalogs;

    private ApiDianCatalog(Dictionary<string, Dictionary<string, CatalogEntry>> catalogs) => _catalogs = catalogs;

    public static ApiDianCatalog Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "catalogs.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var catalogs = new Dictionary<string, Dictionary<string, CatalogEntry>>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            catalogs[property.Name] = property.Value.Deserialize<Dictionary<string, CatalogEntry>>(options)
                ?? throw new InvalidDataException($"Catalog {property.Name} is empty");
        }

        return new ApiDianCatalog(catalogs);
    }

    /// <summary>Devuelve la entrada o falla con el catálogo y el ID, para que una fila faltante sea evidente.</summary>
    public CatalogEntry Get(string catalog, string id)
    {
        if (!_catalogs.TryGetValue(catalog, out var entries))
        {
            throw new KeyNotFoundException($"Unknown catalog '{catalog}'");
        }

        return entries.TryGetValue(id, out var entry)
            ? entry
            : throw new KeyNotFoundException($"Catalog '{catalog}' has no ID {id}; add it to catalogs.json from the ApiDian CSV");
    }

    public CatalogEntry Get(string catalog, int id) => Get(catalog, id.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
