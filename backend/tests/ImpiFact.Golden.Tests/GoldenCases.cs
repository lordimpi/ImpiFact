namespace ImpiFact.Golden.Tests;

/// <summary>
/// Ubica los casos de referencia en tests/golden: XML y CUFE generados por el PHP original (ApiDian), que
/// ImpiFact debe reproducir byte a byte. Se capturan con tools/golden-capture.
/// </summary>
public static class GoldenCases
{
    /// <summary>Carpetas de casos de tests/golden/invoice que tienen una entrada y un XML esperado.</summary>
    public static IReadOnlyList<string> Invoices()
    {
        var invoiceRoot = Path.Combine(FindGoldenRoot(), "invoice");
        return Directory.EnumerateDirectories(invoiceRoot)
            .Where(dir => File.Exists(Path.Combine(dir, "input.json")) && File.Exists(Path.Combine(dir, "expected.xml")))
            .OrderBy(dir => dir, StringComparer.Ordinal)
            .ToList();
    }

    private static string FindGoldenRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "golden");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("tests/golden not found above " + AppContext.BaseDirectory);
    }
}
