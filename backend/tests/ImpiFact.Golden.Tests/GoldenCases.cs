namespace ImpiFact.Golden.Tests;

/// <summary>Locates the golden fixtures under tests/golden, captured by tools/golden-capture.</summary>
public static class GoldenCases
{
    /// <summary>Case folders of tests/golden/invoice that have an input and an expected XML.</summary>
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
