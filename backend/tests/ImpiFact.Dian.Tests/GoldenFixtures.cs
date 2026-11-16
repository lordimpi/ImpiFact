namespace ImpiFact.Dian.Tests;

/// <summary>
/// Un caso de referencia ("golden"): el XML y el CUFE/CUDE que generó ApiDian (el PHP original) y que
/// ImpiFact debe reproducir byte a byte. Se capturan con tools/golden-capture.
/// </summary>
public sealed record GoldenFixture(string DocumentType, string Name, string ExpectedXml, string ExpectedCode)
{
    public override string ToString() => $"{DocumentType}/{Name}";
}

public static class GoldenFixtures
{
    public static IReadOnlyList<GoldenFixture> All()
    {
        var root = FindGoldenRoot();
        return Directory.EnumerateDirectories(root)
            .SelectMany(typeDir => Directory.EnumerateDirectories(typeDir)
                .Where(caseDir => File.Exists(Path.Combine(caseDir, "expected.xml")))
                .Select(caseDir => new GoldenFixture(
                    Path.GetFileName(typeDir),
                    Path.GetFileName(caseDir),
                    File.ReadAllText(Path.Combine(caseDir, "expected.xml")),
                    File.ReadAllText(Path.Combine(caseDir, "expected.cufe")).Trim())))
            .OrderBy(f => f.DocumentType, StringComparer.Ordinal)
            .ThenBy(f => f.Name, StringComparer.Ordinal)
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
