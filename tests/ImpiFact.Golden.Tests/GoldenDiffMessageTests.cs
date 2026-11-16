namespace ImpiFact.Golden.Tests;

public sealed class GoldenDiffMessageTests
{
    [Test]
    public void Reports_the_first_differing_line_with_both_values()
    {
        var message = InvoiceGoldenTests.DescribeFirstDifference("a\n  <b>1.00</b>\nc\n", "a\n  <b>1.01</b>\nc\n");

        Assert.That(message, Does.Contain("line 2"));
        Assert.That(message, Does.Contain("expected: [  <b>1.00</b>]"));
        Assert.That(message, Does.Contain("actual:   [  <b>1.01</b>]"));
    }

    [Test]
    public void Reports_a_missing_line_at_the_end()
    {
        var message = InvoiceGoldenTests.DescribeFirstDifference("a\nb", "a");

        Assert.That(message, Does.Contain("line 2"));
        Assert.That(message, Does.Contain("actual:   [<end of document>]"));
    }
}
