using ImpiFact.Application.Features.Documents.Calculate;
using static ImpiFact.Application.Tests.Features.Documents.Calculate.CalculationSamples;

namespace ImpiFact.Application.Tests.Features.Documents.Calculate;

/// <summary>
/// El cálculo de totales contra los casos golden (solicitudes reales que generó ApiDian, el PHP original, y que la DIAN
/// aceptó): con solo las líneas, los descuentos y cargos y los impuestos de cada caso, el cálculo da los mismos valores
/// que quedaron escritos en el XML golden (cada importe con 2 decimales), y esos valores, llevados a la emisión, pasan
/// el validador de totales de la DIAN.
/// </summary>
public sealed class GoldenCalculationTests
{
    /// <summary>
    /// La única diferencia con un caso golden, y por qué. El caso <c>rounding-raw-decimals</c> prueba que el PHP escribe
    /// con 2 decimales lo que el cliente mandó sin redondear: su total con impuestos (4918.11352695509) es la suma de los
    /// valores sin redondear, así que en el XML queda 4918.11, un centavo menos que la suma de lo que el mismo XML
    /// escribe (valor bruto 4133.69 + IVA 784.43 = 4918.12). El cálculo suma los valores ya redondeados, como manda la
    /// regla AU06 del anexo técnico sobre el XML: sumar sin redondear puede quedar justo al otro lado del medio peso y
    /// la DIAN lo rechazaría (lo muestra
    /// <see cref="DocumentTotalsCalculatorTests.Summing_unrounded_line_values_can_cross_half_a_peso_and_the_dian_rejects_it"/>).
    /// </summary>
    private static readonly Dictionary<string, Dictionary<string, string>> KnownDeviations = new()
    {
        ["invoice/rounding-raw-decimals"] = new()
        {
            ["totals.tax_inclusive_amount=4918.11"] = "totals.tax_inclusive_amount=4918.12",
            ["totals.payable_amount=4918.11"] = "totals.payable_amount=4918.12",
        },
    };

    [Test]
    public void All_fourteen_golden_cases_are_found() => Assert.That(GoldenCases().Count(), Is.EqualTo(14));

    [TestCaseSource(typeof(CalculationSamples), nameof(GoldenCases))]
    public void Calculating_from_the_golden_lines_reproduces_every_golden_value(string name)
    {
        var legacy = GoldenInput(name);
        var spec = SpecOf(name);
        var expected = Describe(legacy, spec)
            .Select(value => KnownDeviations.GetValueOrDefault(name)?.GetValueOrDefault(value) ?? value)
            .ToList();

        var result = DocumentTotalsCalculator.Calculate(RequestFrom(legacy, spec));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That(Describe(result.Value), Is.EqualTo(expected));
    }

    [TestCaseSource(typeof(CalculationSamples), nameof(GoldenCases))]
    public void The_calculated_golden_values_pass_the_issue_totals_validation(string name)
    {
        var spec = SpecOf(name);
        var request = RequestFrom(GoldenInput(name), spec);

        var calculation = DocumentTotalsCalculator.Calculate(request).Value;
        var issued = ValidateAsIssued(request, calculation, spec);

        Assert.That(issued.IsSuccess, Is.True, Describe(issued));
    }

    [TestCaseSource(typeof(CalculationSamples), nameof(GoldenCases))]
    public void Every_golden_draft_passes_the_request_validation(string name)
    {
        var request = RequestFrom(GoldenInput(name), SpecOf(name));

        var validation = new CalculateDocumentTotalsValidator().Validate(request);

        Assert.That(validation.Errors.Select(error => $"{error.PropertyName}: {error.ErrorMessage}"), Is.Empty);
    }
}
