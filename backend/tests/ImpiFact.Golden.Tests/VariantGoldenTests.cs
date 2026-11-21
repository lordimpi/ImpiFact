namespace ImpiFact.Golden.Tests;

/// <summary>
/// Casos de referencia de las variantes de la fase 7a (exportación, contingencia, AIU, mandato, transporte, documento
/// soporte, eventos RADIAN y sector salud), capturados de ApiDian con tools/golden-capture antes de que ImpiFact tenga
/// sus builders. Cada caso queda pendiente (ignorado) hasta la tarea que escribe su builder; esa tarea reemplaza su
/// entrada de <see cref="Pending"/> por una prueba como <see cref="InvoiceGoldenTests"/>. Su case.json dice de qué
/// solicitud de Postman sale.
/// </summary>
public sealed class VariantGoldenTests
{
    /// <summary>Tipos de documento con prueba propia (los builders ya existen).</summary>
    private static readonly string[] Covered =
    [
        "attached-document", "credit-note", "debit-note", "invoice",
        .. InvoiceVariantGoldenTests.Types.Select(type => type.Type),
    ];

    /// <summary>Carpeta de tests/golden -> tarea de odd/tasks/fase-7a-community.md que escribe su builder.</summary>
    private static readonly (string Type, string Task, string Builder)[] Pending =
    [
        ("support-document", "T4", "support document builder"),
        ("sd-credit-note", "T4", "support-document adjustment note builder"),
        ("invoice-contingency", "T5", "contingency type 03 invoice builder"),
        ("invoice-contingency-4", "T5", "contingency type 04 invoice builder"),
        ("event", "T6", "RADIAN event (ApplicationResponse) builder"),
        ("invoice-health", "T7", "health-sector invoice builder"),
    ];

    public static IEnumerable<TestCaseData> Cases() =>
        Pending.SelectMany(pending => GoldenCases.OfType(pending.Type).Select(dir =>
            new TestCaseData(pending.Task, pending.Builder)
                .SetArgDisplayNames(pending.Type + "/" + Path.GetFileName(dir))));

    [TestCaseSource(nameof(Cases))]
    public void Builder_matches_ApiDian_xml_and_code(string task, string builder) =>
        Assert.Ignore($"Pending {task}: the {builder} does not exist yet");

    [TestCaseSource(nameof(PendingTypes))]
    public void Every_pending_variant_has_captured_cases(string documentType) =>
        Assert.That(GoldenCases.OfType(documentType), Is.Not.Empty);

    // Una carpeta nueva en tests/golden necesita una prueba o una entrada pendiente; si no, sus casos no se usan.
    [Test]
    public void Every_golden_type_folder_is_tested_or_pending()
    {
        var known = Covered.Concat(Pending.Select(pending => pending.Type));

        Assert.That(GoldenCases.TypeFolders(), Is.EquivalentTo(known));
    }

    private static IEnumerable<string> PendingTypes() => Pending.Select(pending => pending.Type);
}
