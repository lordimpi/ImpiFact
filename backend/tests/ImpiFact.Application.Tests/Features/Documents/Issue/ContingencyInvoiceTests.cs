using System.Text;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Tests.Features.Documents.Issue;

/// <summary>
/// Contingency invoices through the use case (phase 7a, T5; php-rules, section 12). Type 03 transcribes a paper invoice
/// with its own type 3 resolution and the CUDE with the software PIN (annex 1.9, 11.1.4, Nota-2). Type 04 keeps the
/// numbering of the sales invoice (annex 1.9, 12.2), so it uses the type 1 resolution, its consecutive and the CUFE with
/// its technical key, and is stored signed as <see cref="DocumentStatus.ContingencyPending"/> without being sent.
/// </summary>
public sealed class ContingencyInvoiceTests
{
    private const string PaperBackup = "invoice-contingency";

    private const string DianContingency = "invoice-contingency-4";

    [Test]
    public async Task The_golden_type_03_issues_the_php_xml_and_cude_with_its_own_resolution()
    {
        using var harness = IssueHarness.Create();
        var caseDir = Golden(PaperBackup);

        var result = await harness.IssueAsync(PaperBackup, IssueHarness.GoldenInput(caseDir));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That((result.Value.Cude, result.Value.Cufe), Is.EqualTo((ExpectedCode(caseDir), (string?)null)), "The PHP answers cude (php-rules 12.1, item 3).");
        Assert.That(Unsigned(result), Is.EqualTo(ExpectedUnsigned(caseDir)));
        var graph = harness.SingleGraph;
        Assert.Multiple(() =>
        {
            Assert.That((graph.Document.DocumentTypeId, graph.Document.TypeOperationId, graph.Document.UuidAlgorithm), Is.EqualTo((3, 10, "CUDE-SHA384")));
            Assert.That(graph.Document.ResolutionId, Is.EqualTo((Guid?)harness.ContingencyResolution.Id));
            Assert.That(graph.Document.Status, Is.EqualTo(DocumentStatus.Queued));
            Assert.That(graph.Document.Extensions?["variant"]?.GetValue<string>(), Is.EqualTo("contingency"));
            Assert.That(graph.Document.Extensions?["paper_document_type_code"]?.GetValue<string>(), Is.EqualTo("01"));
            Assert.That(graph.References.Select(reference => (reference.Kind, reference.Number, reference.IssueDate)),
                Is.EqualTo(new[] { (DocumentReferenceKind.AdditionalDocumentReference, (string?)"CTG201", (DateOnly?)new DateOnly(2026, 11, 14)) }));
            Assert.That(result.Value.Message, Is.EqualTo("Factura de Contingencia #SETP990000201 generada con éxito"));
            Assert.That(harness.Db.Messages.Select(message => message.Type), Is.EqualTo(new[] { OutboxMessageTypes.SendDocumentToDian }));
            Assert.That(harness.Db.NotBefore, Is.EqualTo(new DateTimeOffset?[] { null }), "A type 03 is sent right away.");
            Assert.That(harness.Plans.Checks, Is.EqualTo(new[] { (harness.CompanyId, DocumentKind.Invoice) }));
        });
    }

    [TestCase("AdditionalDocumentReferenceID", "El campo AdditionalDocumentReferenceID es obligatorio.")]
    [TestCase("AdditionalDocumentReferenceDate", "El campo AdditionalDocumentReferenceDate es obligatorio.")]
    [TestCase("AdditionalDocumentReferenceTypeDocument", "El campo AdditionalDocumentReferenceTypeDocument es obligatorio.")]
    [TestCase("number", "El campo number es obligatorio.")]
    [TestCase("time", "El campo time es obligatorio.")]
    public async Task A_type_03_requires_the_paper_invoice_its_number_and_time(string field, string message)
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync(PaperBackup, IssueHarness.Edited(Golden(PaperBackup), json => json.Remove(field)));

        AssertValidation(result, field, message);
        Assert.That(harness.Db.Graphs, Is.Empty);
    }

    [TestCase("AdditionalDocumentReferenceDate", "14/11/2026", "AdditionalDocumentReferenceDate no corresponde al formato Y-m-d.")]
    [TestCase("AdditionalDocumentReferenceTypeDocument", "1", "AdditionalDocumentReferenceTypeDocument es inválido.")]
    [TestCase("AdditionalDocumentReferenceTypeDocument", "XX", "AdditionalDocumentReferenceTypeDocument es inválido.")]
    public async Task A_type_03_rejects_a_malformed_paper_invoice_reference(string field, string value, string message)
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync(PaperBackup, IssueHarness.Edited(Golden(PaperBackup), json => json[field] = value));

        AssertValidation(result, field, message);
    }

    // The other operations answered 500 in the PHP (php-rules 9.2, item 7); the contingency invoices keep operation 10.
    [TestCase(PaperBackup)]
    [TestCase(DianContingency)]
    public async Task A_contingency_invoice_keeps_operation_10(string kind)
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync(kind, IssueHarness.Edited(Golden(kind), json => json["type_operation_id"] = 9));

        AssertValidation(result, "type_operation_id", "type operation id es inválido.");
    }

    [Test]
    public async Task The_golden_type_04_is_stored_pending_with_the_annex_cufe_and_the_invoice_numbering()
    {
        using var harness = IssueHarness.Create();
        var caseDir = Golden(DianContingency);

        var result = await harness.IssueAsync(DianContingency, IssueHarness.GoldenInput(caseDir));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        var unsigned = Unsigned(result);
        var graph = harness.SingleGraph;
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Cufe, Has.Length.EqualTo(96));
            Assert.That(result.Value.Cufe, Is.Not.EqualTo(ExpectedCode(caseDir)), "The PHP value used the software PIN (case.json).");
            Assert.That(result.Value.Cude, Is.Null);
            Assert.That(unsigned, Does.Contain("<cbc:InvoiceTypeCode>04</cbc:InvoiceTypeCode>"));
            Assert.That(unsigned, Does.Contain($"schemeName=\"CUFE-SHA384\">{result.Value.Cufe}</cbc:UUID>"));
            Assert.That(unsigned, Does.Contain("<sts:InvoiceAuthorization>18760000001</sts:InvoiceAuthorization>"));
            Assert.That(unsigned, Does.Contain("<cbc:Note>ESTA ES UNA PRUEBA DE NOTA DE DETALLE DE LINEA.</cbc:Note>"));
            Assert.That((graph.Document.DocumentTypeId, graph.Document.UuidAlgorithm, graph.Document.Status),
                Is.EqualTo((12, "CUFE-SHA384", DocumentStatus.ContingencyPending)));
            Assert.That(graph.Document.ResolutionId, Is.EqualTo((Guid?)harness.InvoiceResolution.Id));
            Assert.That(harness.Issuers.Requests, Is.EqualTo(new[] { (1, DocumentKind.Invoice) }), "It reads the type 1 resolutions.");
            Assert.That(result.Value.Document.Status, Is.EqualTo("ContingencyPending"));
            Assert.That(result.Value.Message, Is.EqualTo("Factura electrónica de Venta - tipo 04 #SETP990000301 generada con éxito"));
            Assert.That(harness.Plans.Checks, Is.EqualTo(new[] { (harness.CompanyId, DocumentKind.Invoice) }));
        });
    }

    // No DIAN call when issuing, and no simulated DIAN answer: the Worker's first automatic attempt waits for the 30 minutes
    // the annex gives to monitor the DIAN service again (annex 1.9, 12.2).
    [Test]
    public async Task A_type_04_schedules_its_first_automatic_attempt_after_the_monitoring_interval()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync(DianContingency, IssueHarness.GoldenInput(Golden(DianContingency)));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That(harness.Db.Messages.Select(message => (message.Type, (message.Payload as SendDocumentToDianPayload)?.DocumentId)),
            Is.EqualTo(new[] { (OutboxMessageTypes.SendDocumentToDian, (Guid?)harness.SingleGraph.Document.Id) }));
        Assert.That(harness.Db.NotBefore, Is.EqualTo(new DateTimeOffset?[] { IssueHarness.Now + ContingencyDelivery.RetryInterval }));
        Assert.That(harness.Db.Files.Select(file => file.Kind), Is.EquivalentTo(new[] { DocumentFileKind.RequestJson, DocumentFileKind.SignedXml }),
            "No AttachedDocument nor PDF before the DIAN accepts it (php-rules 7, row 15).");
    }

    [Test]
    public async Task A_type_04_takes_the_next_number_of_the_invoice_consecutive()
    {
        using var harness = IssueHarness.Create();
        var invoice = await harness.IssueAsync("invoice", IssueHarness.Edited(IssueHarness.FirstInvoice, json => json.Remove("number")));

        var contingency = await harness.IssueAsync(DianContingency, IssueHarness.Edited(Golden(DianContingency), json => json.Remove("number")));

        Assert.That(contingency.IsSuccess, Is.True, Describe(contingency));
        Assert.That((invoice.Value.Document.Number, contingency.Value.Document.Number), Is.EqualTo((990000000L, 990000001L)));
    }

    // Shared numbering, per company: a type 04 cannot repeat the number of a type 01 invoice, nor the other way around.
    [Test]
    public async Task A_type_04_and_a_type_01_invoice_never_share_a_number()
    {
        using var harness = IssueHarness.Create();
        var invoice = await harness.IssueAsync("invoice", IssueHarness.Edited(IssueHarness.FirstInvoice, json => json["number"] = 990000500));
        var pending = await harness.IssueAsync(DianContingency, IssueHarness.Edited(Golden(DianContingency), json => json["number"] = 990000600));

        var sameAsInvoice = await harness.IssueAsync(DianContingency, IssueHarness.Edited(Golden(DianContingency), json => json["number"] = 990000500));
        var sameAsPending = await harness.IssueAsync("invoice", IssueHarness.Edited(IssueHarness.FirstInvoice, json => json["number"] = 990000600));

        Assert.That((invoice.IsSuccess, pending.IsSuccess), Is.EqualTo((true, true)));
        Assert.That((sameAsInvoice.Error.Code, sameAsPending.Error.Code), Is.EqualTo(("document.number_in_use", "document.number_in_use")));
        Assert.That(sameAsInvoice.Error.Message, Does.StartWith("El número SETP990000500 ya lo usa otro documento"));
    }

    [Test]
    public async Task Sending_pending_queues_every_pending_type_04_of_the_company_now()
    {
        var delivery = new FakePendingDelivery();
        var companyId = Guid.CreateVersion7();
        var first = delivery.AddPending(companyId, "SETP", 990000301);
        var second = delivery.AddPending(companyId, "FV", 7);
        delivery.AddPending(Guid.CreateVersion7(), "SETP", 990000302);
        using var harness = IssueHarness.Create();

        var result = await SendPendingAsync(harness, delivery, companyId, new SendPendingContingencyInvoicesCommand(null, null));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Success, Is.True);
            Assert.That(result.Value.Message, Is.EqualTo("Envios de documentos de contingencia tipo 4 realizados con exito."));
            Assert.That(result.Value.Documents.Select(document => (document.Id, document.FullNumber, document.Status)),
                Is.EqualTo(new[] { (first.Id, "SETP990000301", "ContingencyPending"), (second.Id, "FV7", "ContingencyPending") }));
            Assert.That(delivery.Queries, Is.EqualTo(new[] { (companyId, (string?)null, (long?)null) }));
            Assert.That(harness.Db.Messages.Select(message => ((message.Payload as SendDocumentToDianPayload)?.DocumentId, message.CompanyId)),
                Is.EqualTo(new[] { ((Guid?)first.Id, (Guid?)companyId), (second.Id, companyId) }));
            Assert.That(harness.Db.NotBefore, Is.EqualTo(new DateTimeOffset?[] { null, null }));
        });
    }

    [Test]
    public async Task Sending_pending_passes_the_prefix_and_number_filters()
    {
        var delivery = new FakePendingDelivery();
        using var harness = IssueHarness.Create();

        var result = await SendPendingAsync(harness, delivery, harness.CompanyId, new SendPendingContingencyInvoicesCommand("SETP", "990000301"));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That(delivery.Queries, Is.EqualTo(new[] { (harness.CompanyId, (string?)"SETP", (long?)990000301) }));
    }

    [Test]
    public async Task Without_pending_documents_the_php_message_is_kept()
    {
        using var harness = IssueHarness.Create();

        var result = await SendPendingAsync(harness, new FakePendingDelivery(), harness.CompanyId, new SendPendingContingencyInvoicesCommand(null, null));

        Assert.That(result.IsSuccess, Is.True, Describe(result));
        Assert.That((result.Value.Message, result.Value.Documents.Count),
            Is.EqualTo(("No existen registros de documentos de contingencia tipo 4 para realizar envios....", 0)));
        Assert.That(harness.Db.Messages, Is.Empty);
    }

    // php-rules 12.3, item 1: a number without its prefix is refused with the PHP message.
    [Test]
    public async Task A_number_without_prefix_is_refused()
    {
        var validation = await new SendPendingContingencyInvoicesCommandValidator().ValidateAsync(new SendPendingContingencyInvoicesCommand(null, "990000301"));

        Assert.That(validation.Errors.Select(error => (error.PropertyName, error.ErrorMessage)), Is.EqualTo(new[]
        {
            ("prefix", "Para hacer envios los envios pendientes debe al menos suministrar el prefijo de las facturas de contingencia tipo 4 que desea enviar...."),
        }));
    }

    [Test]
    public async Task A_number_that_is_not_an_integer_is_refused()
    {
        var validation = await new SendPendingContingencyInvoicesCommandValidator().ValidateAsync(new SendPendingContingencyInvoicesCommand("SETP", "12a"));

        Assert.That(validation.Errors.Select(error => (error.PropertyName, error.ErrorMessage)),
            Is.EqualTo(new[] { ("number", "number debe ser un número entero.") }));
    }

    // php-rules 12.3, item 2: the sending checks the company and its certificate, not the plan.
    [Test]
    public async Task Sending_pending_needs_an_active_company_with_a_certificate()
    {
        using var harness = IssueHarness.Create();
        var delivery = new FakePendingDelivery();
        delivery.AddPending(harness.CompanyId, "SETP", 990000301);
        harness.SetContext(context => context with { Certificate = null });

        var withoutCertificate = await SendPendingAsync(harness, delivery, harness.CompanyId, new SendPendingContingencyInvoicesCommand(null, null));
        harness.SetContext(context => context with { Company = context.Company with { IsActive = false } });
        var inactive = await SendPendingAsync(harness, delivery, harness.CompanyId, new SendPendingContingencyInvoicesCommand(null, null));

        Assert.That((withoutCertificate.Error.Code, inactive.Error.Code), Is.EqualTo(("certificate.missing", "company.inactive")));
        Assert.That(harness.Db.Messages, Is.Empty);
        Assert.That(harness.Plans.Checks, Is.Empty);
    }

    private static Task<Result<SendPendingContingencyInvoicesResponse>> SendPendingAsync(
        IssueHarness harness, FakePendingDelivery delivery, Guid companyId, SendPendingContingencyInvoicesCommand command)
    {
        harness.Issuers.Contexts[(companyId, 1)] = harness.Issuers.Contexts[(harness.CompanyId, 1)] with
        {
            Company = harness.Issuers.Contexts[(harness.CompanyId, 1)].Company with { Id = companyId },
        };
        return new SendPendingContingencyInvoicesHandler(new IssueTenant(companyId), harness.Issuers, delivery, harness.Db, harness.Clock)
            .HandleAsync(command, CancellationToken.None);
    }

    private static string Golden(string kind) => GoldenFiles.Cases(kind).Single();

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

    private static string Describe<T>(Result<T> result) =>
        result.IsSuccess ? "" : $"{result.Error.Code}: {result.Error.Message} {string.Join(" | ", result.Error.Fields)}";

    /// <summary>The pending type 04 documents of each company; it records every query with its filters.</summary>
    private sealed class FakePendingDelivery : IDocumentDelivery
    {
        private readonly List<Document> documents = [];

        public List<(Guid CompanyId, string? Prefix, long? Number)> Queries { get; } = [];

        public Document AddPending(Guid companyId, string prefix, long number)
        {
            var document = new Document
            {
                Id = Guid.CreateVersion7(),
                CompanyId = companyId,
                DocumentTypeId = DocumentFamilies.DianContingencyInvoiceType,
                Prefix = prefix,
                Number = number,
                FullNumber = prefix + number,
                Status = DocumentStatus.ContingencyPending,
                CustomerSnapshot = new CustomerSnapshot { IdentificationNumber = "222222222222", Name = "Consumidor final" },
            };
            documents.Add(document);
            return document;
        }

        public Task<IReadOnlyList<Document>> ListContingencyPendingAsync(Guid companyId, string? prefix, long? number, CancellationToken ct)
        {
            Queries.Add((companyId, prefix, number));
            return Task.FromResult<IReadOnlyList<Document>>(documents
                .Where(document => document.CompanyId == companyId && (prefix is null || document.Prefix == prefix)
                    && (number is null || document.Number == number))
                .ToList());
        }

        public Task<Document?> FindForUpdateAsync(Guid companyId, Guid documentId, CancellationToken ct) => throw new NotSupportedException();

        public Task<int> NextSubmissionAttemptAsync(Guid companyId, Guid documentId, CancellationToken ct) => throw new NotSupportedException();

        public void AddSubmission(DianSubmission submission) => throw new NotSupportedException();

        public Task<DateTimeOffset?> FirstPossibleArrivalAsync(Guid companyId, Guid documentId, Guid? exceptSubmissionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> InvalidSendMessagesAsync(Guid companyId, Guid documentId, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
