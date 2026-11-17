namespace ImpiFact.Golden.Tests;

/// <summary>
/// Ubica los casos de referencia en tests/golden: XML y CUFE/CUDE generados por el PHP original (ApiDian),
/// que ImpiFact debe reproducir byte a byte. Se capturan con tools/golden-capture. El CUFE (código único de
/// la factura electrónica) identifica una factura; el CUDE (código único de documento electrónico), una
/// nota crédito o débito.
/// </summary>
public static class GoldenCases
{
    /// <summary>Carpetas de casos de tests/golden/invoice que tienen una entrada y un XML esperado.</summary>
    public static IReadOnlyList<string> Invoices() => Of("invoice");

    /// <summary>Carpetas de casos de tests/golden/credit-note.</summary>
    public static IReadOnlyList<string> CreditNotes() => Of("credit-note");

    /// <summary>Carpetas de casos de tests/golden/debit-note.</summary>
    public static IReadOnlyList<string> DebitNotes() => Of("debit-note");

    /// <summary>
    /// Carpetas de casos de tests/golden/attached-document (AttachedDocument: el contenedor electrónico que se
    /// entrega al comprador). Su XML esperado se llama expected-attached-document.xml y no expected.xml, para
    /// que las pruebas de facturas y notas que recorren todo tests/golden no lo tomen como uno de sus casos.
    /// </summary>
    public static IReadOnlyList<string> AttachedDocuments() => Of("attached-document", AttachedDocumentExpectedFile);

    public const string AttachedDocumentExpectedFile = "expected-attached-document.xml";

    private static List<string> Of(string documentType, string expectedFile = "expected.xml")
    {
        var typeRoot = Path.Combine(FindGoldenRoot(), documentType);
        return Directory.EnumerateDirectories(typeRoot)
            .Where(dir => File.Exists(Path.Combine(dir, "input.json")) && File.Exists(Path.Combine(dir, expectedFile)))
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
