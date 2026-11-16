using System.Text;
using System.Text.Json.Nodes;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Tests.Features.Documents.Issue;

/// <summary>
/// Emisión de factura, nota crédito y nota débito por el caso de uso con puertos en memoria: paridad golden del XML y
/// del CUFE/CUDE (códigos únicos de factura y de documento electrónico), numeración (supuesto B2), idempotencia,
/// revisiones de la empresa y lo que queda guardado.
/// </summary>
public sealed class IssueDocumentTests
{
    private const string SignatureSlotInFixture = "<ext:ExtensionContent></ext:ExtensionContent>";

    public static IEnumerable<TestCaseData> GoldenCases() =>
        new[] { "invoice", "credit-note", "debit-note" }.SelectMany(type => GoldenFiles.Cases(type)
            .Select(dir => new TestCaseData(type, dir).SetArgDisplayNames(type + "/" + Path.GetFileName(dir))));

    // La puerta de la fase 3 por el caso de uso: el XML sin firmar y el CUFE/CUDE de la respuesta son los del PHP.
    [TestCaseSource(nameof(GoldenCases))]
    public async Task Golden_input_issues_the_php_xml_and_code(string type, string caseDir)
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync(type, IssueHarness.GoldenInput(caseDir));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        var response = result.Value;
        var expectedCode = File.ReadAllText(Path.Combine(caseDir, "expected.cufe")).Trim();
        Assert.That(type == "invoice" ? response.Cufe : response.Cude, Is.EqualTo(expectedCode));
        var unsigned = Encoding.UTF8.GetString(Convert.FromBase64String(response.UnsignedInvoiceXml!));
        Assert.That(unsigned, Is.EqualTo(ExpectedUnsigned(caseDir)));
        Assert.That(response.QrStr, Is.EqualTo("https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=" + expectedCode));
    }

    [Test]
    public async Task Issued_invoice_is_queued_with_its_files_and_outbox_message()
    {
        using var harness = IssueHarness.Create();
        var input = IssueHarness.GoldenInput(IssueHarness.FirstInvoice);

        var result = await harness.IssueAsync("invoice", input);

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        var graph = harness.SingleGraph;
        var document = graph.Document;
        Assert.Multiple(() =>
        {
            Assert.That(document.Status, Is.EqualTo(DocumentStatus.Queued));
            Assert.That((document.Prefix, document.Number, document.FullNumber), Is.EqualTo(("SETP", 990000002L, "SETP990000002")));
            Assert.That(document.Cufe, Is.EqualTo(result.Value.Cufe));
            Assert.That(document.UuidAlgorithm, Is.EqualTo("CUFE-SHA384"));
            Assert.That(document.ResolutionId, Is.EqualTo(harness.InvoiceResolution.Id));
            Assert.That((document.IssueDate, document.IssueTime), Is.EqualTo((new DateOnly(2026, 11, 14), new TimeOnly(10, 5, 0))));
            Assert.That(document.IssuedAt, Is.EqualTo(new DateTimeOffset(2026, 11, 14, 15, 5, 0, TimeSpan.Zero)));
            Assert.That(document.CustomerSnapshot.IdentificationNumber, Is.EqualTo("800000001"));
            Assert.That(graph.Lines, Has.Count.EqualTo(1));
            Assert.That(graph.Taxes, Has.Count.EqualTo(1));
            Assert.That(graph.LineTaxes, Has.Count.EqualTo(1));
            Assert.That(graph.Payments.Single().PaymentFormId, Is.EqualTo(1));
            Assert.That(harness.Db.Files.Select(file => file.Kind), Is.EquivalentTo(new[] { DocumentFileKind.RequestJson, DocumentFileKind.SignedXml }));
            Assert.That(harness.Db.Files.Single(file => file.Kind == DocumentFileKind.RequestJson).Content, Is.EqualTo(input));
            Assert.That(harness.Db.Messages.Single().Type, Is.EqualTo("SendDocumentToDian"));
            Assert.That(harness.Db.Messages.Single().Payload, Is.EqualTo(new SendDocumentToDianPayload(document.Id, null)));
            Assert.That(harness.Db.Messages.Single().CompanyId, Is.EqualTo(harness.CompanyId));
            Assert.That(harness.Certificates.Loads.Single(), Is.EqualTo((harness.CompanyId, "cert-ref")));
            Assert.That(result.Value.Success, Is.True);
            Assert.That(result.Value.Message, Is.EqualTo("Factura de Venta Nacional #SETP990000002 generada con éxito"));
            Assert.That(result.Value.CertificateDaysLeft, Is.EqualTo(new DateOnly(2035, 12, 31).DayNumber - new DateOnly(2026, 11, 16).DayNumber));
            Assert.That(result.Value.ResolutionDaysLeft, Is.EqualTo(new DateOnly(2030, 1, 19).DayNumber - new DateOnly(2026, 11, 16).DayNumber));
            Assert.That(result.Value.ResponseDian, Is.Null);
            Assert.That(result.Value.Document.Status, Is.EqualTo("Queued"));
        });
        var signed = Encoding.UTF8.GetString(harness.Db.Files.Single(file => file.Kind == DocumentFileKind.SignedXml).Content);
        Assert.That(signed, Does.Contain("<ds:Signature"));
        Assert.That(Convert.FromBase64String(result.Value.InvoiceXml!), Is.EqualTo(Encoding.UTF8.GetBytes(signed)));
    }

    [Test]
    public async Task Without_number_the_next_number_of_the_sequence_is_taken_and_checked()
    {
        using var harness = IssueHarness.Create();
        var input = IssueHarness.Edited(IssueHarness.FirstInvoice, json => json.Remove("number"));

        var first = await harness.IssueAsync("invoice", input);
        var second = await harness.IssueAsync("invoice", input);

        Assert.That(first.IsSuccess && second.IsSuccess, Is.True, Describe(first) + Describe(second));
        Assert.That(harness.Db.Graphs.Select(graph => graph.Document.Number), Is.EqualTo(new[] { 990000000L, 990000001L }));
        Assert.That(harness.Db.Calls, Has.All.StartsWith("take "));
    }

    [Test]
    public async Task Sequence_skips_numbers_already_used_by_other_documents()
    {
        using var harness = IssueHarness.Create();
        Assert.That((await harness.IssueAsync("invoice", IssueHarness.Edited(IssueHarness.FirstInvoice, json => json["number"] = 990000000))).IsSuccess);
        harness.Db.Sequences[NumberingScope.ForResolution(harness.InvoiceResolution.Id)] = 990000000;

        var result = await harness.IssueAsync("invoice", IssueHarness.Edited(IssueHarness.FirstInvoice, json => json.Remove("number")));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That(result.Value.Document.Number, Is.EqualTo(990000001L));
    }

    [Test]
    public async Task Sequence_past_the_end_of_the_range_is_rejected()
    {
        using var harness = IssueHarness.Create();
        harness.Db.Sequences[NumberingScope.ForResolution(harness.InvoiceResolution.Id)] = 995000001;

        var result = await harness.IssueAsync("invoice", IssueHarness.Edited(IssueHarness.FirstInvoice, json => json.Remove("number")));

        AssertValidation(result, "number", "Se agotó el rango de numeración de la resolución. Configure una resolución nueva.");
    }

    [Test]
    public async Task Resolution_without_a_sequence_gives_a_clear_error()
    {
        using var harness = IssueHarness.Create();
        harness.Db.Sequences.Clear();

        var automatic = await harness.IssueAsync("invoice", IssueHarness.Edited(IssueHarness.FirstInvoice, json => json.Remove("number")));
        var requested = await harness.IssueAsync("invoice", IssueHarness.GoldenInput(IssueHarness.FirstInvoice));

        Assert.That(automatic.Error.Code, Is.EqualTo("numbering.sequence_missing"));
        Assert.That(requested.Error.Code, Is.EqualTo("numbering.sequence_missing"));
    }

    // Revisión antes de reservar: un número fuera del rango nunca mueve el consecutivo.
    [TestCase(989999999L)]
    [TestCase(long.MaxValue)]
    public async Task Number_out_of_range_is_rejected_before_reserving(long number)
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("invoice", IssueHarness.Edited(IssueHarness.FirstInvoice, json => json["number"] = number));

        AssertValidation(result, "number", "number tiene que estar entre 990000000 - 995000000 o no se establecio el atributo que define el minimo y maximo.");
        Assert.That(harness.Db.Calls, Is.Empty);
    }

    [TestCase("2019-01-18", "date debe ser una fecha posterior o igual a 2019-01-19.")]
    [TestCase("2030-01-20", "date debe ser una fecha anterior o igual a 2030-01-19.")]
    public async Task Date_outside_the_resolution_validity_is_rejected(string date, string message)
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("invoice", IssueHarness.Edited(IssueHarness.FirstInvoice, json => json["date"] = date));

        AssertValidation(result, "date", message);
    }

    [Test]
    public async Task Same_number_with_the_same_request_returns_the_existing_document()
    {
        using var harness = IssueHarness.Create();
        var input = IssueHarness.GoldenInput(IssueHarness.FirstInvoice);
        var first = await harness.IssueAsync("invoice", input);

        var again = await harness.IssueAsync("invoice", input);

        Assert.That(again.IsSuccess, Is.True, Describe(again));
        Assert.That(harness.Db.Graphs, Has.Count.EqualTo(1));
        Assert.That(again.Value.Document.Id, Is.EqualTo(first.Value.Document.Id));
        Assert.That(again.Value.Document.Replayed, Is.True);
        Assert.That(again.Value.Cufe, Is.EqualTo(first.Value.Cufe));
        Assert.That(again.Value.InvoiceXml, Is.EqualTo(first.Value.InvoiceXml));
    }

    [Test]
    public async Task Same_number_with_another_request_is_a_conflict()
    {
        using var harness = IssueHarness.Create();
        await harness.IssueAsync("invoice", IssueHarness.GoldenInput(IssueHarness.FirstInvoice));

        var other = await harness.IssueAsync("invoice", IssueHarness.Edited(IssueHarness.FirstInvoice, json => json["notes"] = "otra"));

        Assert.That(other.Error.Type, Is.EqualTo(ErrorType.Conflict));
        Assert.That(other.Error.Code, Is.EqualTo("document.number_in_use"));
        Assert.That(other.Error.Message, Does.Contain("SETP990000002"));
    }

    [Test]
    public async Task Same_idempotency_key_returns_the_same_document_and_another_body_is_a_conflict()
    {
        using var harness = IssueHarness.Create();
        var input = IssueHarness.Edited(IssueHarness.FirstInvoice, json => json.Remove("number"));
        var first = await harness.IssueAsync("invoice", input, idempotencyKey: "key-1");

        var again = await harness.IssueAsync("invoice", input, idempotencyKey: "key-1");
        var other = await harness.IssueAsync("invoice", IssueHarness.Edited(IssueHarness.FirstInvoice, json => json["notes"] = "x"), idempotencyKey: "key-1");

        Assert.That(again.Value.Document.Id, Is.EqualTo(first.Value.Document.Id));
        Assert.That(harness.Db.Graphs, Has.Count.EqualTo(1));
        Assert.That(other.Error.Code, Is.EqualTo("idempotency.key_reused"));
        Assert.That(other.Error.Type, Is.EqualTo(ErrorType.Conflict));
    }

    [Test]
    public async Task Missing_resolution_answers_like_the_php()
    {
        using var harness = IssueHarness.Create();

        var otherPrefix = await harness.IssueAsync("invoice", IssueHarness.Edited(IssueHarness.FirstInvoice, json => json["prefix"] = "FE"));
        harness.Resolutions = _ => [];
        harness.SetContext(context => context);
        var none = await harness.IssueAsync("credit-note", IssueHarness.GoldenInput(IssueHarness.FirstCreditNote));

        AssertValidation(otherPrefix, "type_document_id", "La resolución no está configurada.");
        AssertValidation(none, "type_document_id", "La resolución no está configurada.");
    }

    [Test]
    public async Task Inactive_resolution_is_not_used()
    {
        using var harness = IssueHarness.Create();
        harness.Resolutions = _ => [harness.InvoiceResolution with { IsActive = false }];
        harness.SetContext(context => context);

        var result = await harness.IssueAsync("invoice", IssueHarness.GoldenInput(IssueHarness.FirstInvoice));

        AssertValidation(result, "type_document_id", "La resolución no está configurada.");
    }

    [Test]
    public async Task Certificate_missing_or_expired_and_software_missing_are_rejected()
    {
        using var harness = IssueHarness.Create();
        var input = IssueHarness.GoldenInput(IssueHarness.FirstInvoice);

        harness.SetContext(context => context with { Certificate = null });
        var missing = await harness.IssueAsync("invoice", input);
        harness.SetContext(context => context with { Certificate = new IssuerCertificateFixture().ExpiresToday });
        var expired = await harness.IssueAsync("invoice", input);
        harness.SetContext(context => context with { Certificate = new IssuerCertificateFixture().Valid, Software = null });
        var noSoftware = await harness.IssueAsync("invoice", input);

        Assert.Multiple(() =>
        {
            Assert.That(missing.Error.Code, Is.EqualTo("certificate.missing"));
            Assert.That(expired.Error.Code, Is.EqualTo("certificate.expired"));
            Assert.That(expired.Error.Message, Is.EqualTo("El certificado digital ya se encuentra vencido..."));
            Assert.That(noSoftware.Error.Code, Is.EqualTo("software.missing"));
            Assert.That(harness.Db.Graphs, Is.Empty);
        });
    }

    [Test]
    public async Task Inactive_or_unknown_company_cannot_issue()
    {
        using var harness = IssueHarness.Create();
        var input = IssueHarness.GoldenInput(IssueHarness.FirstInvoice);

        var unknown = await harness.IssueAsync("invoice", input, companyId: Guid.CreateVersion7());
        harness.SetContext(context => context with { Company = context.Company with { IsActive = false } });
        var inactive = await harness.IssueAsync("invoice", input);

        Assert.That(unknown.Error.Code, Is.EqualTo("company.inactive"));
        Assert.That(inactive.Error.Type, Is.EqualTo(ErrorType.Forbidden));
    }

    // Decisión 18 y supuesto B6: lo que la DIAN rechazaría se rechaza antes de tomar el número.
    [Test]
    public async Task Totals_the_dian_would_reject_answer_422_before_taking_a_number()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("invoice", IssueHarness.Edited(IssueHarness.FirstInvoice, json => json["legal_monetary_totals"]!["payable_amount"] = "1.00"));

        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Validation));
        Assert.That(result.Error.Fields.Select(field => field.Field), Does.Contain("legal_monetary_totals.payable_amount"));
        Assert.That(harness.Db.Calls, Is.Empty);
    }

    [Test]
    public async Task Unknown_catalog_ids_are_reported_per_field()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("invoice", IssueHarness.Edited(IssueHarness.FirstInvoice, json =>
        {
            json["customer"]!["type_regime_id"] = 999;
            json["invoice_lines"]![0]!["unit_measure_id"] = 99999;
        }));

        Assert.That(result.Error.Fields, Is.EquivalentTo(new[]
        {
            new FieldError("customer.type_regime_id", "customer.type regime id es inválido."),
            new FieldError("invoice_lines.0.unit_measure_id", "invoice lines.0.unit measure id es inválido."),
        }));
    }

    [Test]
    public async Task Request_validation_uses_the_laravel_messages()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("invoice", IssueHarness.Edited(IssueHarness.FirstInvoice, json =>
        {
            json["type_document_id"] = 4;
            json["date"] = "14/11/2026";
            json["customer"]!["dv"] = "7";
            json["customer"]!.AsObject().Remove("name");
            json["invoice_lines"]![0]!["invoiced_quantity"] = "uno";
            json["invoice_lines"]![0]!["free_of_charge_indicator"] = "si";
        }));

        Assert.That(result.Error.Fields, Is.SupersetOf(new[]
        {
            new FieldError("type_document_id", "type document id es inválido."),
            new FieldError("date", "date no corresponde al formato Y-m-d."),
            new FieldError("customer.dv", "Error, el digito de verificacion no es valido para este NIT."),
            new FieldError("customer.name", "El campo customer.name es obligatorio."),
            new FieldError("invoice_lines.0.invoiced_quantity", "invoice lines.0.invoiced quantity debe ser numérico."),
            new FieldError("invoice_lines.0.free_of_charge_indicator", "El campo invoice lines.0.free of charge indicator debe tener un valor verdadero o falso."),
        }));
    }

    [Test]
    public async Task Debit_note_requires_the_customer_email_and_a_note_without_reference_requires_its_period()
    {
        using var harness = IssueHarness.Create();

        var debit = await harness.IssueAsync("debit-note", IssueHarness.Edited(IssueHarness.FirstDebitNote, json => json["customer"]!.AsObject().Remove("email")));
        var credit = await harness.IssueAsync("credit-note", IssueHarness.Edited(IssueHarness.FirstCreditNote, json => json["type_operation_id"] = 8));

        AssertValidation(debit, "customer.email", "El campo customer.email es obligatorio a menos que customer.identification number esté en 222222222222.");
        AssertValidation(credit, "invoice_period", "El campo invoice period es obligatorio cuando type operation id es 8.");
    }

    // Las plantillas del PHP quitan CR, LF y «|» de cada valor; el XML de ImpiFact también.
    [Test]
    public async Task Carriage_returns_line_feeds_and_pipes_are_removed_from_texts()
    {
        using var harness = IssueHarness.Create();
        var clean = await harness.IssueAsync("invoice", IssueHarness.GoldenInput(IssueHarness.FirstInvoice));
        using var other = IssueHarness.Create();

        var dirty = await other.IssueAsync("invoice", IssueHarness.Edited(IssueHarness.FirstInvoice, json =>
        {
            json["customer"]!["name"] = json["customer"]!["name"]!.GetValue<string>().Replace(" ", "\r\n ", StringComparison.Ordinal);
            json["invoice_lines"]![0]!["description"] = "|" + json["invoice_lines"]![0]!["description"]!.GetValue<string>() + "\n";
        }));

        Assert.That(dirty.IsSuccess, Is.True, Describe(dirty));
        Assert.That(dirty.Value.Cufe, Is.EqualTo(clean.Value.Cufe));
        Assert.That(dirty.Value.UnsignedInvoiceXml, Is.EqualTo(clean.Value.UnsignedInvoiceXml));
    }

    [Test]
    public async Task Credit_note_is_linked_to_the_company_invoice_with_that_cufe()
    {
        using var harness = IssueHarness.Create();
        var invoice = await harness.IssueAsync("invoice", IssueHarness.GoldenInput(IssueHarness.FirstInvoice));

        var note = await harness.IssueAsync("credit-note", IssueHarness.Edited(IssueHarness.FirstCreditNote, json =>
            json["billing_reference"]!["uuid"] = invoice.Value.Cufe));
        var unlinked = await harness.IssueAsync("credit-note", IssueHarness.Edited(IssueHarness.FirstCreditNote, json => json["number"] = 5));

        Assert.That(note.IsSuccess, Is.True, Describe(note));
        Assert.That(harness.Db.Graphs[1].Document.ReferenceDocumentId, Is.EqualTo(invoice.Value.Document.Id));
        Assert.That(harness.Db.Graphs[1].References.Single().Uuid, Is.EqualTo(invoice.Value.Cufe));
        Assert.That(harness.Db.Graphs[2].Document.ReferenceDocumentId, Is.Null);
        Assert.That(note.Value.Success, Is.Null, "The PHP note response has no success field.");
        Assert.That(note.Value.Cude, Is.Not.Null);
        Assert.That(note.Value.Cufe, Is.Null);
        Assert.That(note.Value.ResolutionDaysLeft, Is.Null);
        Assert.That(harness.Db.Graphs[1].Document.UuidAlgorithm, Is.EqualTo("CUDE-SHA384"));
    }

    [Test]
    public async Task Smtp_password_is_not_stored_with_the_request()
    {
        using var harness = IssueHarness.Create();
        var input = IssueHarness.Edited(IssueHarness.FirstInvoice, json => json["smtp_parameters"] = new JsonObject { ["password"] = "secreto-smtp" });

        var result = await harness.IssueAsync("invoice", input);

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        var stored = Encoding.UTF8.GetString(harness.Db.Files.Single(file => file.Kind == DocumentFileKind.RequestJson).Content);
        Assert.That(stored, Does.Not.Contain("secreto-smtp"));
        Assert.That(stored, Does.Contain("invoice_lines"));
    }

    private static string ExpectedUnsigned(string caseDir)
    {
        var text = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(caseDir, "expected.xml")));
        return text.Replace(SignatureSlotInFixture, "<ext:ExtensionContent/>", StringComparison.Ordinal);
    }

    private static void AssertValidation(Result<IssueDocumentResponse> result, string field, string message)
    {
        Assert.That(result.IsSuccess, Is.False, "Expected a validation error.");
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Validation));
        Assert.That(result.Error.Fields, Does.Contain(new FieldError(field, message)), string.Join(" | ", result.Error.Fields));
    }

    private static string Describe(Result<IssueDocumentResponse> result) =>
        result.IsSuccess ? "" : $"{result.Error.Code}: {result.Error.Message} {string.Join(" | ", result.Error.Fields)}";

    private sealed class IssuerCertificateFixture
    {
        public Abstractions.Documents.IssuerCertificate Valid { get; } = new("cert-ref", new DateTimeOffset(2036, 1, 1, 0, 0, 0, TimeSpan.Zero));

        /// <summary>Vence hoy en Colombia (16-nov-2026 a las 23:00, hora de Colombia).</summary>
        public Abstractions.Documents.IssuerCertificate ExpiresToday { get; } = new("cert-ref", new DateTimeOffset(2026, 11, 17, 4, 0, 0, TimeSpan.Zero));
    }
}
