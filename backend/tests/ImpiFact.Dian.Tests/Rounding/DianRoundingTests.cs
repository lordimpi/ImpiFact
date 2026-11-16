using ImpiFact.Dian.Rounding;

namespace ImpiFact.Dian.Tests.Rounding;

public class DianRoundingTests
{
    // Values captured from the PHP golden case invoice/rounding-raw-decimals: PHP's number_format
    // rounds half away from zero. 6.965 is 6.96499... as a double, so it must be handled as decimal.
    [TestCase("2.185", "2.19")]
    [TestCase("6.965", "6.97")]
    [TestCase("0.34825", "0.35")]
    [TestCase("4115.222921811", "4115.22")]
    [TestCase("4918.11352695509", "4918.11")]
    [TestCase("784.07735514409", "784.08")]
    [TestCase("1500000", "1500000.00")]
    [TestCase("0", "0.00")]
    [TestCase("-2.185", "-2.19")]
    public void FormatAmount_rounds_half_away_from_zero_to_two_decimals(string input, string expected)
    {
        Assert.That(DianRounding.FormatAmount(DianRounding.Parse(input)), Is.EqualTo(expected));
    }

    [TestCase("3.333333", "3.333333")]
    [TestCase("1", "1.000000")]
    [TestCase("12.5", "12.500000")]
    [TestCase("0.0000005", "0.000001")]
    public void FormatQuantity_uses_six_decimals(string input, string expected)
    {
        Assert.That(DianRounding.FormatQuantity(DianRounding.Parse(input)), Is.EqualTo(expected));
    }

    [TestCase("15", "15.000")]
    [TestCase("0.966", "0.966")]
    [TestCase("0.9665", "0.967")]
    public void FormatWithholding_uses_three_decimals(string input, string expected)
    {
        Assert.That(DianRounding.FormatWithholding(DianRounding.Parse(input)), Is.EqualTo(expected));
    }

    [Test]
    public void Parse_ignores_the_current_culture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("es-CO");
            Assert.That(DianRounding.Parse("1234.5"), Is.EqualTo(1234.5m));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}
