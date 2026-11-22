using System.Text;
using System.Text.Json;
using ImpiFact.Dian.Radian;

namespace ImpiFact.Golden.Tests;

/// <summary>
/// RADIAN events (ApplicationResponse, template 88), phase 7a T6: converting each input.json of tests/golden/event,
/// ImpiFact must produce the same bytes as expected.xml and the same event CUDE as expected.cufe, compared exactly like
/// <see cref="InvoiceGoldenTests"/>. No substitution: the QR the capture flagged as a suspected PHP bug (production
/// catalog and the CUFE of the invoice) is what Anexo Técnico 1.9 asks for in rule AAB36 (php-rules 16.4, item 8).
/// </summary>
public sealed class EventGoldenTests
{
    /// <summary>Folders of tests/golden covered here.</summary>
    internal static readonly string[] Types = ["event"];

    private static readonly ApiDianCatalog Catalog = ApiDianCatalog.Load();

    public static IEnumerable<TestCaseData> Events() =>
        GoldenCases.OfType("event").Select(dir => new TestCaseData(dir).SetArgDisplayNames("event/" + Path.GetFileName(dir)));

    [TestCaseSource(nameof(Events))]
    public void Event_builder_matches_ApiDian_xml_and_cude(string caseDir)
    {
        var result = RadianEventBuilder.Build(new ApiDianEventAdapter(Catalog).Map(Input(caseDir)));

        Assert.That(result.Cude, Is.EqualTo(File.ReadAllText(Path.Combine(caseDir, "expected.cufe")).Trim()), "CUDE");
        var expected = InvoiceGoldenTests.ExpectedBeforeSigning(caseDir);
        if (!expected.AsSpan().SequenceEqual(new UTF8Encoding(false).GetBytes(result.Xml)))
        {
            Assert.Fail(InvoiceGoldenTests.DescribeFirstDifference(Encoding.UTF8.GetString(expected), result.Xml));
        }
    }

    // php-rules 16.4, item 2: the event number is the NIT of the receiver, the invoice number and the event code.
    [Test]
    public void The_event_number_joins_the_receiver_nit_the_invoice_number_and_the_code()
    {
        var caseDir = GoldenCases.OfType("event")[0];

        var result = RadianEventBuilder.Build(new ApiDianEventAdapter(Catalog).Map(Input(caseDir)));

        Assert.That(result.Number, Is.EqualTo("800000002FPRU1001030"));
    }

    private static JsonElement Input(string caseDir)
    {
        using var input = JsonDocument.Parse(File.ReadAllText(Path.Combine(caseDir, "input.json"), Encoding.UTF8));
        return input.RootElement.Clone();
    }
}
