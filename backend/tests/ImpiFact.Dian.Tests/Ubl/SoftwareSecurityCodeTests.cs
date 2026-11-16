using ImpiFact.Dian.Ubl;

namespace ImpiFact.Dian.Tests.Ubl;

public sealed class SoftwareSecurityCodeTests
{
    [Test]
    public void Matches_the_code_of_the_golden_invoice_single_line_iva19()
    {
        var code = SoftwareSecurityCode.Compute("3f9d8c2e-5b1a-4e7f-9c3d-2a6b8e1f0d47", "12345", "SETP990000002");

        Assert.That(code, Is.EqualTo("621f5ca4483774508e45a9c9d6389283ecd24420c4abe31f1b834ec80492cc10968cccf1218e8ffab378039b1a7b4486"));
    }
}
