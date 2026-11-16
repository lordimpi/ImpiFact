using System.Text;
using System.Text.Json;
using ImpiFact.Dian.Ubl;

namespace ImpiFact.Golden.Tests;

/// <summary>
/// ApiDian golden cases: input.json mapped through the fixed capture context must produce the
/// same bytes as expected.xml and the same CUFE as expected.cufe.
/// </summary>
public sealed class InvoiceGoldenTests
{
    /// <summary>
    /// expected.xml is the signed XML with the ds:Signature element cut out by a regular
    /// expression, which leaves the second ext:ExtensionContent as an explicit start/end pair. The
    /// XML before signing has that element empty, written <c>&lt;ext:ExtensionContent/&gt;</c> like
    /// every other empty element. This is the only normalization, and it is applied to exactly
    /// that one spot.
    /// </summary>
    internal const string SignatureSlotInFixture = "<ext:ExtensionContent></ext:ExtensionContent>";

    internal const string SignatureSlotBeforeSigning = "<ext:ExtensionContent/>";

    private static readonly ApiDianCatalog Catalog = ApiDianCatalog.Load();

    public static IEnumerable<TestCaseData> Cases() =>
        GoldenCases.Invoices().Select(dir => new TestCaseData(dir).SetName("invoice/" + Path.GetFileName(dir)));

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

    /// <summary>Reports the first differing line with both values, making whitespace visible.</summary>
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
