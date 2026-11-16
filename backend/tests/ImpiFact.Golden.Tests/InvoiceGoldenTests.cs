using System.Text;
using System.Text.Json;
using ImpiFact.Dian.Ubl;

namespace ImpiFact.Golden.Tests;

/// <summary>
/// Casos de referencia de ApiDian (XML y CUFE generados por el PHP original): al convertir input.json con
/// la configuración fija de captura, ImpiFact debe producir los mismos bytes que expected.xml y el mismo
/// CUFE (código único de la factura) que expected.cufe.
/// </summary>
public sealed class InvoiceGoldenTests
{
    /// <summary>
    /// expected.xml es el XML firmado por ApiDian al que se le quitó el elemento ds:Signature con una
    /// expresión regular. Eso deja el lugar reservado para la firma (el segundo ext:ExtensionContent)
    /// escrito con etiqueta de apertura y de cierre. El XML que produce ImpiFact antes de firmar tiene ese
    /// elemento vacío y lo escribe <c>&lt;ext:ExtensionContent/&gt;</c>, como cualquier otro elemento vacío.
    /// Este es el único ajuste que se hace al comparar, y solo en ese punto.
    /// </summary>
    internal const string SignatureSlotInFixture = "<ext:ExtensionContent></ext:ExtensionContent>";

    internal const string SignatureSlotBeforeSigning = "<ext:ExtensionContent/>";

    private static readonly ApiDianCatalog Catalog = ApiDianCatalog.Load();

    public static IEnumerable<TestCaseData> Cases() =>
        GoldenCases.Invoices().Select(dir => new TestCaseData(dir).SetArgDisplayNames("invoice/" + Path.GetFileName(dir)));

    [TestCaseSource(nameof(Cases))]
    public void Builder_matches_ApiDian_xml_and_cufe(string caseDir)
    {
        var result = Build(caseDir);

        var expectedCufe = File.ReadAllText(Path.Combine(caseDir, "expected.cufe")).Trim();
        Assert.That(result.Cufe, Is.EqualTo(expectedCufe), "CUFE");

        var expected = ExpectedBeforeSigning(caseDir);
        var actual = new UTF8Encoding(false).GetBytes(result.Xml);
        if (!expected.AsSpan().SequenceEqual(actual))
        {
            Assert.Fail(DescribeFirstDifference(Encoding.UTF8.GetString(expected), result.Xml));
        }
    }

    // La URL del QR llega en el modelo de entrada (viene de la configuración por ambiente), no fija en el código.
    [Test]
    public void Qr_code_uses_the_base_url_given_in_the_input()
    {
        var caseDir = GoldenCases.Invoices().First();
        using var input = JsonDocument.Parse(File.ReadAllText(Path.Combine(caseDir, "input.json"), Encoding.UTF8));
        var invoice = new ApiDianInvoiceAdapter(Catalog).Map(input.RootElement) with
        {
            QrCodeBaseUrl = "https://qr.example.test/search?documentkey=",
        };

        var result = InvoiceUblBuilder.Build(invoice);

        Assert.That(result.Xml, Does.Contain($"<sts:QRCode>https://qr.example.test/search?documentkey={result.Cufe}</sts:QRCode>"));
    }

    private static InvoiceUblResult Build(string caseDir)
    {
        using var input = JsonDocument.Parse(File.ReadAllText(Path.Combine(caseDir, "input.json"), Encoding.UTF8));
        var invoice = new ApiDianInvoiceAdapter(Catalog).Map(input.RootElement);
        return InvoiceUblBuilder.Build(invoice);
    }

    private static byte[] ExpectedBeforeSigning(string caseDir)
    {
        var text = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(caseDir, "expected.xml")));
        var first = text.IndexOf(SignatureSlotInFixture, StringComparison.Ordinal);
        if (first < 0 || text.IndexOf(SignatureSlotInFixture, first + 1, StringComparison.Ordinal) >= 0)
        {
            Assert.Fail($"expected.xml must contain exactly one {SignatureSlotInFixture} (the removed signature)");
        }

        var normalized = string.Concat(text.AsSpan(0, first), SignatureSlotBeforeSigning, text.AsSpan(first + SignatureSlotInFixture.Length));
        return new UTF8Encoding(false).GetBytes(normalized);
    }

    /// <summary>
    /// Informa la primera línea que difiere, con el valor esperado y el obtenido, y muestra los caracteres
    /// invisibles \r y \t como texto.
    /// </summary>
    internal static string DescribeFirstDifference(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var count = Math.Max(expectedLines.Length, actualLines.Length);
        for (var i = 0; i < count; i++)
        {
            var e = i < expectedLines.Length ? expectedLines[i] : "<end of document>";
            var a = i < actualLines.Length ? actualLines[i] : "<end of document>";
            if (!string.Equals(e, a, StringComparison.Ordinal))
            {
                return $"First difference at line {i + 1}:{Environment.NewLine}" +
                       $"  expected: [{Visible(e)}]{Environment.NewLine}" +
                       $"  actual:   [{Visible(a)}]";
            }
        }

        return "Documents differ only in encoding or line endings";
    }

    private static string Visible(string line) => line.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal);
}
