using System.Text;
using System.Text.Json.Nodes;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Domain.Common;

namespace ImpiFact.Application.Tests.Features.Documents.Issue;

/// <summary>
/// Export invoice through the use case (phase 7a, T3; php-rules, section 11): the golden XML and CUFE in pesos, the
/// foreign currency of annex 1.9 (FAD15b, FAR01-FAR07) with a required exchange rate, the Incoterm list, the supplement
/// K complete or absent, and that it is a type 2 invoice with its own resolution that counts in the invoice plan.
/// </summary>
public sealed class ExportInvoiceTests
{
    private const string Kind = "invoice-export";

    [Test]
    public async Task The_golden_export_in_pesos_issues_the_php_xml_and_cufe_as_a_type_2_invoice()
    {
        using var harness = IssueHarness.Create();
        var caseDir = Golden("cop-cif-additional-data");

        var result = await harness.IssueAsync(Kind, IssueHarness.GoldenInput(caseDir));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That(result.Value.Cufe, Is.EqualTo(ExpectedCufe(caseDir)));
        Assert.That(Unsigned(result), Is.EqualTo(ExpectedUnsigned(caseDir)));
        var document = harness.SingleGraph.Document;
        Assert.That((document.DocumentTypeId, document.TypeOperationId, document.UuidAlgorithm), Is.EqualTo((2, 10, "CUFE-SHA384")));
        Assert.That(document.ResolutionId, Is.EqualTo((Guid?)harness.ExportResolution.Id));
        Assert.That(result.Value.Message, Is.EqualTo("Factura de Exportación #SETP990000101 generada con éxito"));
        Assert.That(harness.Plans.Checks, Is.EqualTo(new[] { (harness.CompanyId, DocumentKind.Invoice) }));
    }

    // Annex 1.9: DocumentCurrencyCode in the foreign currency, every @currencyID equal to it except TotalesCop (FAD15b)
    // and cac:PaymentExchangeRate with the client's rate and date (FAR01-FAR07). The PHP wrote pesos (golden usd-fob).
    [Test]
    public async Task An_export_in_dollars_writes_the_annex_currency_fields_with_the_client_rate()
    {
        using var harness = IssueHarness.Create();
        var caseDir = Golden("usd-fob");

        var result = await harness.IssueAsync(Kind, IssueHarness.GoldenInput(caseDir));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That(result.Value.Cufe, Is.EqualTo(ExpectedCufe(caseDir)), "The currency is not part of the CUFE.");
        Assert.That(Unsigned(result), Is.EqualTo(InDollars(ExpectedUnsigned(caseDir), "4100.50", "2026-11-14")));
        var document = harness.SingleGraph.Document;
        Assert.That(document.CurrencyId, Is.EqualTo(149));
        Assert.That(document.ExchangeRate, Is.EqualTo(4100.50m));
    }

    [Test]
    public async Task The_company_currency_sent_as_idcurrency_is_written_like_the_php()
    {
        using var harness = IssueHarness.Create();
        var caseDir = Golden("cop-cif-additional-data");

        var result = await harness.IssueAsync(Kind, IssueHarness.Edited(caseDir, json => json["idcurrency"] = 35));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That(Unsigned(result), Is.EqualTo(ExpectedUnsigned(caseDir)));
    }

    [TestCase("calculationrate",
        "El campo calculationrate es obligatorio cuando idcurrency es USD y no COP: indique la tasa de cambio a COP (la TRM o la pactada con el comprador). ImpiFact no la consulta en línea ni la supone.")]
    [TestCase("calculationratedate",
        "El campo calculationratedate es obligatorio cuando idcurrency es USD y no COP: indique la fecha de la tasa de cambio (Y-m-d).")]
    public async Task A_foreign_currency_requires_the_exchange_rate_and_its_date(string field, string message)
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync(Kind, IssueHarness.Edited(Golden("usd-fob"), json => json.Remove(field)));

        AssertValidation(result, field, message);
        Assert.That(harness.Db.Graphs, Is.Empty);
    }

    [TestCase("0")]
    [TestCase("-4100.50")]
    public async Task The_exchange_rate_must_be_positive(string rate)
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync(Kind, IssueHarness.Edited(Golden("usd-fob"), json => json["calculationrate"] = rate));

        AssertValidation(result, "calculationrate", "calculationrate debe ser mayor que 0.");
    }

    [Test]
    public async Task The_delivery_terms_are_required()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync(Kind, IssueHarness.Edited(Golden("cop-cif-additional-data"), json => json.Remove("deliveryterms")));

        AssertValidation(result, "deliveryterms", "El campo deliveryterms es obligatorio.");
    }

    [TestCase("special_terms", "El campo deliveryterms.special terms es obligatorio.")]
    [TestCase("loss_risk_responsibility_code", "El campo deliveryterms.loss risk responsibility code es obligatorio.")]
    [TestCase("loss_risk", "El campo deliveryterms.loss risk es obligatorio.")]
    public async Task Each_delivery_term_is_required(string field, string message)
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync(Kind,
            IssueHarness.Edited(Golden("cop-cif-additional-data"), json => json["deliveryterms"]!.AsObject().Remove(field)));

        AssertValidation(result, $"deliveryterms.{field}", message);
    }

    // DPU is in the incoterms catalog but not in the PHP list; it stays rejected (php-rules, section 18, question 3).
    [TestCase("DPU")]
    [TestCase("XYZ")]
    [TestCase("fob")]
    public async Task An_incoterm_outside_the_php_list_is_rejected(string code)
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync(Kind,
            IssueHarness.Edited(Golden("cop-cif-additional-data"), json => json["deliveryterms"]!["loss_risk_responsibility_code"] = code));

        AssertValidation(result, "deliveryterms.loss_risk_responsibility_code",
            "deliveryterms.loss risk responsibility code es inválido. Use uno de estos Incoterms: CFR, CIF, CIP, CPT, DAP, DAT, DDP, EXW, FAS, FCA o FOB.");
        Assert.That(harness.Db.Graphs, Is.Empty);
    }

    // The PHP answered 500 to an incomplete k_supplement (golden usd-fob, case.json); ImpiFact answers 422 per field.
    [TestCase("FctConvCop", "El campo k supplement.FctConvCop es obligatorio cuando k supplement está presente.")]
    [TestCase("responsible_incharge", "El campo k supplement.responsible incharge es obligatorio cuando k supplement está presente.")]
    [TestCase("TotAnticiposCop", "El campo k supplement.TotAnticiposCop es obligatorio cuando k supplement está presente.")]
    public async Task An_incomplete_supplement_k_is_rejected(string field, string message)
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync(Kind,
            IssueHarness.Edited(Golden("cop-cif-additional-data"), json => json["k_supplement"]!.AsObject().Remove(field)));

        AssertValidation(result, $"k_supplement.{field}", message);
        Assert.That(harness.Db.Graphs, Is.Empty);
    }

    [Test]
    public async Task An_empty_supplement_k_lists_every_missing_field()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync(Kind, IssueHarness.Edited(Golden("cop-cif-additional-data"), json => json["k_supplement"] = new JsonObject()));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error.Fields.Count(field => field.Field.StartsWith("k_supplement.", StringComparison.Ordinal)), Is.EqualTo(30));
    }

    [TestCase("0")]
    [TestCase("tres mil")]
    public async Task The_supplement_exchange_rate_must_be_a_positive_number(string rate)
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync(Kind,
            IssueHarness.Edited(Golden("cop-cif-additional-data"), json => json["k_supplement"]!["FctConvCop"] = rate));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error.Fields.Select(field => field.Field), Does.Contain("k_supplement.FctConvCop"));
    }

    // Annex 1.9, section 21: the supplement K is optional. Without it there is no third extension and no exchange rate is
    // fetched or invented (the PHP wrote the online TRM or 4000.00 and then failed).
    [Test]
    public async Task Without_supplement_k_the_export_has_no_third_extension()
    {
        using var harness = IssueHarness.Create();
        var caseDir = Golden("cop-cif-additional-data");

        var result = await harness.IssueAsync(Kind, IssueHarness.Edited(caseDir, json => json.Remove("k_supplement")));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        var xml = Unsigned(result);
        Assert.That(xml, Does.Not.Contain("CustomTagGeneral"));
        Assert.That(xml, Does.Not.Contain("FctConvCop"));
        Assert.That(xml, Does.Contain("<cbc:LossRiskResponsibilityCode>CIF</cbc:LossRiskResponsibilityCode>"));
        Assert.That(xml, Is.EqualTo(WithoutSupplement(ExpectedUnsigned(caseDir))));
        Assert.That(harness.SingleGraph.Document.Extensions?["k_supplement"], Is.Null);
    }

    [Test]
    public async Task The_operation_is_fixed_to_10()
    {
        using var harness = IssueHarness.Create();
        var caseDir = Golden("cop-cif-additional-data");

        var accepted = await harness.IssueAsync(Kind, IssueHarness.Edited(caseDir, json => json["type_operation_id"] = 10));
        var rejected = await harness.IssueAsync(Kind, IssueHarness.Edited(caseDir, json => json["type_operation_id"] = 9));

        Assert.That(accepted.IsSuccess, Is.True, Describe(accepted));
        AssertValidation(rejected, "type_operation_id", "type operation id es inválido.");
    }

    [Test]
    public async Task The_export_route_only_issues_type_2()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync(Kind, IssueHarness.Edited(Golden("cop-cif-additional-data"), json => json["type_document_id"] = 1));

        AssertValidation(result, "type_document_id", "type document id es inválido.");
    }

    // php-rules 11, item 1: the export invoice has its own type 2 resolution and sequence, apart from the type 1 one.
    [Test]
    public async Task The_export_takes_its_number_from_its_own_resolution()
    {
        using var harness = IssueHarness.Create();
        var standard = IssueHarness.Edited(IssueHarness.FirstInvoice, json => json.Remove("number"));
        var export = IssueHarness.Edited(Golden("cop-cif-additional-data"), json => json.Remove("number"));

        var first = await harness.IssueAsync("invoice", standard);
        var second = await harness.IssueAsync(Kind, export);
        var third = await harness.IssueAsync(Kind, export);

        Assert.That(first.IsSuccess && second.IsSuccess && third.IsSuccess, Is.True, Describe(first) + Describe(second) + Describe(third));
        Assert.That(new[] { second.Value.Document.Number, third.Value.Document.Number }, Is.EqualTo(new long[] { 990000000, 990000001 }));
        Assert.That(harness.Db.Graphs.Select(graph => graph.Document.ResolutionId),
            Is.EqualTo(new Guid?[] { harness.InvoiceResolution.Id, harness.ExportResolution.Id, harness.ExportResolution.Id }));
    }

    [Test]
    public async Task The_export_data_is_stored_with_the_document_and_its_lines()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync(Kind, IssueHarness.GoldenInput(Golden("cop-cif-additional-data")));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        var graph = harness.SingleGraph;
        var extensions = graph.Document.Extensions!;
        Assert.That(extensions["variant"]!.GetValue<string>(), Is.EqualTo("export"));
        Assert.That(extensions["deliveryterms"]!.ToJsonString(),
            Is.EqualTo("""{"special_terms":"COSTO SEGURO Y FLETE","loss_risk_responsibility_code":"CIF","loss_risk":"COSTO SEGURO Y FLETE"}"""));
        Assert.That(extensions["k_supplement"]!["FctConvCop"]!.GetValue<string>(), Is.EqualTo("3890"));
        Assert.That(extensions["k_supplement"]!.AsObject(), Has.Count.EqualTo(30));
        Assert.That(graph.Lines[0].Extensions?.ToJsonString(), Is.EqualTo("""{"brandname":"FULLFIT","modelname":"1032"}"""));
    }

    // Previous behaviour: the standard invoice ignores every export field and still matches its golden.
    [Test]
    public async Task A_standard_invoice_keeps_ignoring_the_export_fields()
    {
        using var harness = IssueHarness.Create();
        var export = JsonNode.Parse(IssueHarness.GoldenInput(Golden("cop-cif-additional-data")))!.AsObject();
        var input = IssueHarness.Edited(IssueHarness.FirstInvoice, json =>
        {
            json["deliveryterms"] = export["deliveryterms"]!.DeepClone();
            json["k_supplement"] = export["k_supplement"]!.DeepClone();
            json["customer"]!["municipality_name"] = "MIAMI";
            json["customer"]!["state_name"] = "FLORIDA";
            json["invoice_lines"]![0]!["brandname"] = "FULLFIT";
            json["invoice_lines"]![0]!["modelname"] = "1032";
        });

        var result = await harness.IssueAsync("invoice", input);

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That(Unsigned(result), Is.EqualTo(ExpectedUnsigned(IssueHarness.FirstInvoice)));
        Assert.That(harness.SingleGraph.Document.Extensions, Is.Null);
        Assert.That(harness.SingleGraph.Lines[0].Extensions, Is.Null);
    }

    private static string Golden(string name) => GoldenFiles.Cases("invoice-export").Single(dir => Path.GetFileName(dir) == name);

    private static string ExpectedCufe(string caseDir) => File.ReadAllText(Path.Combine(caseDir, "expected.cufe")).Trim();

    private static string Unsigned(Result<IssueDocumentResponse> result) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(result.Value.UnsignedInvoiceXml!));

    private static string ExpectedUnsigned(string caseDir) =>
        Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(caseDir, "expected.xml")))
            .Replace("<ext:ExtensionContent></ext:ExtensionContent>", "<ext:ExtensionContent/>", StringComparison.Ordinal);

    /// <summary>The golden XML in pesos turned into the annex 1.9 XML in dollars: the only bytes that change.</summary>
    internal static string InDollars(string xml, string rate, string date)
    {
        var exchangeRate = string.Concat(
            "  <cac:PaymentExchangeRate>\n",
            "    <cbc:SourceCurrencyCode>USD</cbc:SourceCurrencyCode>\n",
            "    <cbc:SourceCurrencyBaseRate>1.00</cbc:SourceCurrencyBaseRate>\n",
            "    <cbc:TargetCurrencyCode>COP</cbc:TargetCurrencyCode>\n",
            "    <cbc:TargetCurrencyBaseRate>1.00</cbc:TargetCurrencyBaseRate>\n",
            $"    <cbc:CalculationRate>{rate}</cbc:CalculationRate>\n",
            $"    <cbc:Date>{date}</cbc:Date>\n",
            "  </cac:PaymentExchangeRate>\n");
        var dollars = xml
            .Replace("currencyID=\"COP\"", "currencyID=\"USD\"", StringComparison.Ordinal)
            .Replace("<cbc:DocumentCurrencyCode>COP</cbc:DocumentCurrencyCode>", "<cbc:DocumentCurrencyCode>USD</cbc:DocumentCurrencyCode>", StringComparison.Ordinal);
        var taxTotal = dollars.IndexOf("\n  <cac:TaxTotal>", StringComparison.Ordinal);
        return dollars.Insert(taxTotal + 1, exchangeRate);
    }

    /// <summary>The golden XML without its third ext:UBLExtension (the supplement K).</summary>
    private static string WithoutSupplement(string xml)
    {
        var start = xml.IndexOf("    <ext:UBLExtension>\n      <ext:ExtensionContent>\n        <CustomTagGeneral>", StringComparison.Ordinal);
        var end = xml.IndexOf("  </ext:UBLExtensions>", StringComparison.Ordinal);
        return xml.Remove(start, end - start);
    }

    private static void AssertValidation(Result<IssueDocumentResponse> result, string field, string message)
    {
        Assert.That(result.IsSuccess, Is.False, "Expected a validation error.");
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Validation));
        Assert.That(result.Error.Fields, Does.Contain(new FieldError(field, message)), string.Join(" | ", result.Error.Fields));
    }

    private static string Describe(Result<IssueDocumentResponse> result) =>
        result.IsSuccess ? "" : $"{result.Error.Code}: {result.Error.Message} {string.Join(" | ", result.Error.Fields)}";
}
