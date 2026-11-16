using System.Text.Json;

namespace ImpiFact.Integration.Tests.Model;

/// <summary>
/// Los catálogos DIAN sembrados coinciden id por id con los JSON de <c>Infrastructure/Seed</c> (Modelo de datos
/// detallado, sección 9 y criterio 3 de la sección 11). Lee los archivos fuente del repositorio, no el recurso
/// embebido que usa la semilla, para que un error del cargador no pase inadvertido.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class CatalogSeedTests
{
    /// <summary>Las 29 tablas de catálogo de la paridad Community (sin <c>health_type_document_identifications</c>).</summary>
    public static readonly string[] CatalogTables =
    [
        "countries", "credit_note_discrepancy_responses", "credit_note_discrepancy_responses_sd",
        "debit_note_discrepancy_responses", "departments", "discounts", "events", "incoterms", "languages",
        "municipalities", "payment_forms", "payment_methods", "prepaid_payment_types", "reference_prices", "taxes",
        "type_currencies", "type_discounts", "type_document_identifications", "type_documents", "type_environments",
        "type_generation_transmitions", "type_item_identifications", "type_liabilities", "type_operations",
        "type_organizations", "type_regimes", "type_rejections", "type_spds", "unit_measures",
    ];

    /// <summary>Columnas que el JSON no trae y que la semilla llena con su valor por defecto.</summary>
    private static readonly Dictionary<string, object?> Defaults = new(StringComparer.Ordinal)
    {
        ["is_active"] = true,
        ["valid_from"] = null,
        ["valid_to"] = null,
    };

    [Test]
    public void Every_seed_file_has_a_catalog_table()
    {
        var files = Directory.EnumerateFiles(SeedDirectory(), "*.json").Select(Path.GetFileNameWithoutExtension);

        Assert.That(files, Is.EquivalentTo(CatalogTables));
    }

    [TestCaseSource(nameof(CatalogTables))]
    public async Task Seeded_catalog_equals_its_json_row_by_row(string table)
    {
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(SeedDirectory(), table + ".json")));
        var expected = json.RootElement.EnumerateArray().ToList();
        var actual = await Sql.RowsAsync($"select * from {table} order by id");

        Assert.That(actual, Has.Count.EqualTo(expected.Count), $"{table}: row count");
        var differences = new List<string>();
        for (var i = 0; i < Math.Min(expected.Count, actual.Count); i++)
        {
            Compare(table, expected[i], actual[i], differences);
        }

        Assert.That(differences, Is.Empty);
    }

    private static void Compare(string table, JsonElement expected, IDictionary<string, object?> actual, List<string> differences)
    {
        var id = expected.GetProperty("id").GetInt32();
        var jsonColumns = expected.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        var unexpected = actual.Keys.Where(column => !jsonColumns.Contains(column) && !Defaults.ContainsKey(column));
        differences.AddRange(unexpected.Select(column => $"{table} id {id}: column {column} is not in the JSON"));

        foreach (var property in expected.EnumerateObject())
        {
            var value = actual.TryGetValue(property.Name, out var found) ? found : "<missing column>";
            if (!Equal(property.Value, value))
            {
                differences.Add($"{table} id {id}: {property.Name} = {value ?? "null"}, JSON = {property.Value}");
            }
        }

        foreach (var (column, value) in Defaults)
        {
            if (!Equals(actual.TryGetValue(column, out var found) ? found : "<missing column>", value))
            {
                differences.Add($"{table} id {id}: {column} = {found ?? "null"}, expected {value ?? "null"}");
            }
        }
    }

    private static bool Equal(JsonElement expected, object? actual) => expected.ValueKind switch
    {
        JsonValueKind.Null => actual is null,
        JsonValueKind.String => actual is string text && string.Equals(text, expected.GetString(), StringComparison.Ordinal),
        JsonValueKind.Number => actual is int or long && Convert.ToInt64(actual, System.Globalization.CultureInfo.InvariantCulture) == expected.GetInt64(),
        _ => false,
    };

    private static string SeedDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ImpiFact.slnx")))
            {
                return Path.Combine(dir.FullName, "src", "ImpiFact.Infrastructure", "Seed");
            }
        }

        throw new DirectoryNotFoundException("ImpiFact.slnx not found above " + AppContext.BaseDirectory);
    }
}
