using System.Text;
using System.Text.Json.Nodes;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Tests.Features.Documents.Issue;

/// <summary>
/// Support document (type 11) and its adjustment note (type 13) through the use case (phase 7a, T4; php-rules, sections
/// 13 and 14): the golden XML and CUDS, the support-document software, environment and plan family instead of the
/// invoice ones, the seller rules, the CUDS that ignores a technical key, and the reference of the note to a support
/// document of the company.
/// </summary>
public sealed class SupportDocumentTests
{
    private const string Kind = "support-document";

    private const string NoteKind = "sd-credit-note";

    /// <summary>SoftwareID of the invoice software, which the support-document family must never write.</summary>
    private const string InvoiceSoftwareId = "3f9d8c2e-5b1a-4e7f-9c3d-2a6b8e1f0d47";

    private const string SupportSoftwareId = "7c1e5a90-2b4d-4f3a-8e6c-1d9b0a2f3c58";

    [TestCase("resident-seller", 23, "10")]
    [TestCase("non-resident-seller", 24, "11")]
    public async Task The_golden_support_document_issues_the_php_xml_and_cuds(string name, int operation, string code)
    {
        using var harness = IssueHarness.Create();
        var caseDir = Golden(name);

        var result = await harness.IssueAsync(Kind, IssueHarness.GoldenInput(caseDir));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That(result.Value.Cuds, Is.EqualTo(ExpectedCode(caseDir)));
        Assert.That((result.Value.Cufe, result.Value.Cude), Is.EqualTo(((string?)null, (string?)null)));

        // php-rules 13.2, item 4: without idcurrency the exchange rate carries the day of the request (the harness clock).
        Assert.That(Unsigned(result), Is.EqualTo(ExpectedUnsigned(caseDir).Replace("<cbc:Date>2026-11-21</cbc:Date>", "<cbc:Date>2026-11-16</cbc:Date>",
            StringComparison.Ordinal)));
        Assert.That(Unsigned(result), Does.Contain($"<cbc:CustomizationID>{code}</cbc:CustomizationID>"));
        var document = harness.SingleGraph.Document;
        Assert.That((document.DocumentTypeId, document.TypeOperationId, document.UuidAlgorithm), Is.EqualTo((11, operation, "CUDS-SHA384")));
        Assert.That(document.ResolutionId, Is.EqualTo((Guid?)harness.SupportDocumentResolution.Id));
        Assert.That(document.CustomerSnapshot.IdentificationNumber, Is.EqualTo(name == "resident-seller" ? "800000003" : "XE0000000002"));
        Assert.That(result.Value.UrlInvoiceXml, Is.EqualTo($"DSS-{document.FullNumber}.xml"));
        Assert.That(result.Value.Message, Is.EqualTo($"Documento Soporte Electrónico #{document.FullNumber} generada con éxito"));
    }

    // php-rules 13.1, items 6 and 7: the support-document software, environment and plan family, not the invoice ones.
    [Test]
    public async Task The_support_document_uses_the_support_document_software_environment_and_plan()
    {
        using var harness = IssueHarness.Create();
        harness.SupportDocumentEnvironment = 1;
        harness.SetContext(context => context);

        var result = await harness.IssueAsync(Kind, IssueHarness.GoldenInput(Golden("resident-seller")));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That(harness.Issuers.Requests, Is.EqualTo(new[] { (11, DocumentKind.SupportDocument) }));
        Assert.That(harness.Plans.Checks, Is.EqualTo(new[] { (harness.CompanyId, DocumentKind.SupportDocument) }));
        var xml = Unsigned(result);
        Assert.That(xml, Does.Contain($">{SupportSoftwareId}</sts:SoftwareID>"));
        Assert.That(xml, Does.Not.Contain(InvoiceSoftwareId));
        Assert.That(xml, Does.Contain("<cbc:ProfileExecutionID>1</cbc:ProfileExecutionID>"));
        Assert.That(harness.SingleGraph.Document.TypeEnvironmentId, Is.EqualTo(1));
    }

    // php-rules 13.3, item 2: the PHP wrote a CUFE when the type 11 resolution had a technical key.
    [Test]
    public async Task A_technical_key_on_the_support_document_resolution_is_ignored()
    {
        using var harness = IssueHarness.Create();
        var withKey = harness.SupportDocumentResolution with { TechnicalKey = IssueHarness.TechnicalKey };
        harness.Resolutions = type => type == 11 ? [withKey] : [harness.InvoiceResolution];
        harness.SetContext(context => context);
        var caseDir = Golden("resident-seller");

        var result = await harness.IssueAsync(Kind, IssueHarness.GoldenInput(caseDir));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That(result.Value.Cuds, Is.EqualTo(ExpectedCode(caseDir)));
    }

    [Test]
    public async Task A_support_document_resolution_without_validity_is_not_configured()
    {
        using var harness = IssueHarness.Create();
        var withoutDates = harness.SupportDocumentResolution with { ValidFrom = null, ValidTo = null };
        harness.Resolutions = type => type == 11 ? [withoutDates] : [harness.InvoiceResolution];
        harness.SetContext(context => context);

        var result = await harness.IssueAsync(Kind, IssueHarness.GoldenInput(Golden("resident-seller")));

        AssertValidation(result, "type_document_id", "La resolución no está configurada.");
    }

    [Test]
    public async Task Without_the_support_document_software_it_answers_software_missing()
    {
        using var harness = IssueHarness.Create();
        harness.SetContext(context => context with { Software = null });

        var result = await harness.IssueAsync(Kind, IssueHarness.GoldenInput(Golden("resident-seller")));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error.Code, Is.EqualTo("software.missing"));
        Assert.That(result.Error.Message, Does.StartWith("La empresa no tiene configurado el software de documento soporte"));
    }

    [TestCase("seller", "El campo seller es obligatorio.")]
    [TestCase("seller.postal_zone_code", "El campo seller.postal zone code es obligatorio.")]
    [TestCase("seller.phone", "El campo seller.phone es obligatorio.")]
    [TestCase("seller.merchant_registration", "El campo seller.merchant registration es obligatorio.")]
    [TestCase("invoice_lines.0.start_date", "El campo invoice lines.0.start date es obligatorio.")]
    [TestCase("invoice_lines.0.type_generation_transmition_id", "El campo invoice lines.0.type generation transmition id es obligatorio.")]
    public async Task The_php_required_fields_answer_422(string field, string message)
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync(Kind, IssueHarness.Edited(Golden("resident-seller"), json => Remove(json, field)));

        AssertValidation(result, field, message);
        Assert.That(harness.Db.Graphs, Is.Empty);
    }

    [TestCase("seller.type_document_identification_id", "3", "seller.type document identification id es inválido.")]
    [TestCase("seller.postal_zone_code", "\"63A001\"", "seller.postal zone code debe ser numérico.")]
    [TestCase("type_operation_id", "10", "type operation id es inválido.")]
    [TestCase("type_document_id", "1", "type document id es inválido.")]
    [TestCase("invoice_lines.0.start_date", "\"14/11/2026\"", "invoice lines.0.start date no corresponde al formato Y-m-d.")]
    [TestCase("invoice_lines.0.type_generation_transmition_id", "9", "invoice lines.0.type generation transmition id es inválido.")]
    public async Task Invalid_support_document_values_answer_422(string field, string json, string message)
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync(Kind, IssueHarness.Edited(Golden("resident-seller"), body => Set(body, field, JsonNode.Parse(json))));

        AssertValidation(result, field, message);
        Assert.That(harness.Db.Graphs, Is.Empty);
    }

    [Test]
    public async Task The_golden_adjustment_note_issues_the_cuds_with_the_support_document_software()
    {
        using var harness = IssueHarness.Create();
        await AcceptedSupportDocumentAsync(harness);
        var caseDir = GoldenNote();

        var result = await harness.IssueAsync(NoteKind, IssueHarness.GoldenInput(caseDir));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That(result.Value.Cuds, Is.EqualTo(ExpectedCode(caseDir)));
        Assert.That(result.Value.Success, Is.Null, "Like the notes, the adjustment note has no success flag.");

        // php-rules 14, item 7: ImpiFact writes the support-document SoftwareID where the PHP wrote the invoice one.
        Assert.That(Unsigned(result), Is.EqualTo(ExpectedUnsigned(caseDir).Replace(
            $">{InvoiceSoftwareId}</sts:SoftwareID>", $">{SupportSoftwareId}</sts:SoftwareID>", StringComparison.Ordinal)));
        var note = harness.Db.Graphs[1];
        Assert.That((note.Document.DocumentTypeId, note.Document.TypeOperationId, note.Document.UuidAlgorithm), Is.EqualTo((13, 10, "CUDS-SHA384")));
        Assert.That(note.Document.ReferenceDocumentId, Is.EqualTo(harness.Db.Graphs[0].Document.Id));
        Assert.That(result.Value.UrlInvoiceXml, Is.EqualTo("NDSNS-NDS1.xml"));
        Assert.That(harness.Issuers.Requests[^1], Is.EqualTo((13, DocumentKind.SupportDocument)));
        Assert.That(harness.Plans.Checks[^1], Is.EqualTo((harness.CompanyId, DocumentKind.SupportDocument)));
    }

    // php-rules 14, item 8: the PHP stored the adjustment note with the invoice environment.
    [Test]
    public async Task The_adjustment_note_is_stored_in_the_support_document_environment()
    {
        using var harness = IssueHarness.Create();
        harness.SupportDocumentEnvironment = 1;
        harness.SetContext(context => context);
        var cuds = await AcceptedSupportDocumentAsync(harness);

        // In production the support document has another CUDS (ProfileExecutionID 1), so the note references that one.
        var result = await harness.IssueAsync(NoteKind, IssueHarness.Edited(GoldenNote(), json => json["billing_reference"]!["uuid"] = cuds));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That(harness.Db.Graphs[1].Document.TypeEnvironmentId, Is.EqualTo(1));
        Assert.That(Unsigned(result), Does.Contain("<cbc:ProfileExecutionID>1</cbc:ProfileExecutionID>"));
    }

    // php-rules 14, item 2 (Mejora): the referenced CUDS must be a support document of the company.
    [Test]
    public async Task An_adjustment_note_to_an_unknown_support_document_answers_422()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync(NoteKind, IssueHarness.GoldenInput(GoldenNote()));

        AssertValidation(result, "billing_reference.uuid", IssueErrors.SupportDocumentReferenceNotFoundMessage);
        Assert.That(result.Error.Code, Is.EqualTo("document.reference_not_found"));
        Assert.That(harness.Db.Graphs, Is.Empty);
    }

    [Test]
    public async Task An_adjustment_note_to_a_support_document_not_yet_accepted_answers_422_except_in_the_test_set()
    {
        using var harness = IssueHarness.Create();
        var issued = await harness.IssueAsync(Kind, IssueHarness.GoldenInput(Golden("resident-seller")));
        Assert.That(issued.IsSuccess, Is.True, Describe(issued));

        var refused = await harness.IssueAsync(NoteKind, IssueHarness.GoldenInput(GoldenNote()));
        var testSet = await harness.IssueAsync(NoteKind, IssueHarness.GoldenInput(GoldenNote()), testSetId: "2b9c6d1e-4f3a-4b8c-9d7e-1a2b3c4d5e6f");

        AssertValidation(refused, "billing_reference.uuid", IssueErrors.SupportDocumentReferenceNotAcceptedMessage);
        Assert.That(testSet.IsSuccess, Is.True, Describe(testSet));
    }

    [Test]
    public async Task An_adjustment_note_with_another_number_for_the_cuds_answers_422()
    {
        using var harness = IssueHarness.Create();
        await AcceptedSupportDocumentAsync(harness);

        var result = await harness.IssueAsync(NoteKind, IssueHarness.Edited(GoldenNote(), json => json["billing_reference"]!["number"] = "SEDS984000009"));

        AssertValidation(result, "billing_reference.number", IssueErrors.SupportDocumentReferenceNumberMismatchMessage);
    }

    // php-rules 14, item 3: the PHP accepted 6 and crashed in findOrFail; the catalog has 5 codes.
    [TestCase("6")]
    [TestCase("0")]
    public async Task A_discrepancy_code_outside_the_support_document_catalog_answers_422(string code)
    {
        using var harness = IssueHarness.Create();
        await AcceptedSupportDocumentAsync(harness);

        var result = await harness.IssueAsync(NoteKind, IssueHarness.Edited(GoldenNote(), json => json["discrepancyresponsecode"] = int.Parse(code)));

        AssertValidation(result, "discrepancyresponsecode", IssueMessages.Between("discrepancyresponsecode", 1, 5));
    }

    [TestCase("billing_reference", "El campo billing reference es obligatorio.")]
    [TestCase("seller", "El campo seller es obligatorio.")]
    public async Task The_adjustment_note_requires_its_reference_and_seller(string field, string message)
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync(NoteKind, IssueHarness.Edited(GoldenNote(), json => json.Remove(field)));

        AssertValidation(result, field, message);
    }

    [TestCase(23, "10")]
    [TestCase(24, "11")]
    public async Task The_adjustment_note_accepts_the_operations_23_and_24(int operation, string code)
    {
        using var harness = IssueHarness.Create();
        await AcceptedSupportDocumentAsync(harness);

        var result = await harness.IssueAsync(NoteKind, IssueHarness.Edited(GoldenNote(), json => json["type_operation_id"] = operation));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That(Unsigned(result), Does.Contain($"<cbc:CustomizationID>{code}</cbc:CustomizationID>"));
    }

    // The previous behavior of the credit note is unchanged: it keeps the invoice family and accepts an unknown CUFE.
    [Test]
    public async Task The_credit_note_keeps_the_invoice_software_and_family()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("credit-note", IssueHarness.GoldenInput(IssueHarness.FirstCreditNote));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That(harness.Issuers.Requests, Is.EqualTo(new[] { (4, DocumentKind.Invoice) }));
        Assert.That(result.Value.Cuds, Is.Null);
        Assert.That(Unsigned(result), Does.Contain($">{InvoiceSoftwareId}</sts:SoftwareID>"));
    }

    /// <summary>Issues the golden resident support document (the one the golden note references) and marks it accepted.</summary>
    private static async Task<string> AcceptedSupportDocumentAsync(IssueHarness harness)
    {
        var issued = await harness.IssueAsync(Kind, IssueHarness.GoldenInput(Golden("resident-seller")));
        Assert.That(issued.IsSuccess, Is.True, Describe(issued));
        var document = harness.Db.Graphs.Single().Document;
        document.MoveTo(DocumentStatus.Sent);
        document.MoveTo(DocumentStatus.Accepted);
        return issued.Value.Cuds!;
    }

    private static void Remove(JsonObject json, string field)
    {
        var parts = field.Split('.');
        JsonNode node = json;
        foreach (var part in parts[..^1])
        {
            node = int.TryParse(part, out var index) ? node[index]! : node[part]!;
        }

        node.AsObject().Remove(parts[^1]);
    }

    private static void Set(JsonObject json, string field, JsonNode? value)
    {
        var parts = field.Split('.');
        JsonNode node = json;
        foreach (var part in parts[..^1])
        {
            node = int.TryParse(part, out var index) ? node[index]! : node[part]!;
        }

        node[parts[^1]] = value;
    }

    private static string Golden(string name) => GoldenFiles.Cases("support-document").Single(dir => Path.GetFileName(dir) == name);

    private static string GoldenNote() => GoldenFiles.Cases("sd-credit-note").Single();

    private static string ExpectedCode(string caseDir) => File.ReadAllText(Path.Combine(caseDir, "expected.cufe")).Trim();

    private static string Unsigned(Result<IssueDocumentResponse> result) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(result.Value.UnsignedInvoiceXml!));

    private static string ExpectedUnsigned(string caseDir) =>
        Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(caseDir, "expected.xml")))
            .Replace("<ext:ExtensionContent></ext:ExtensionContent>", "<ext:ExtensionContent/>", StringComparison.Ordinal);

    private static void AssertValidation(Result<IssueDocumentResponse> result, string field, string message)
    {
        Assert.That(result.IsSuccess, Is.False, "Expected a validation error.");
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Validation));
        Assert.That(result.Error.Fields, Does.Contain(new FieldError(field, message)), string.Join(" | ", result.Error.Fields));
    }

    private static string Describe(Result<IssueDocumentResponse> result) =>
        result.IsSuccess ? "" : $"{result.Error.Code}: {result.Error.Message} {string.Join(" | ", result.Error.Fields)}";
}
