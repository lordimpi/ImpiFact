using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Tests.Features.Documents.Issue;

/// <summary>
/// Guardas de la emisión de la fase 4: el plan de la empresa (como el PHP, antes de emitir; Modelo de datos detallado,
/// sección 3.6) y, en las notas, que la factura referenciada de la empresa ya esté aceptada por la DIAN (Arquitectura
/// detallada, sección 7). La ruta del set de pruebas se salta las dos, como el <c>testSetStore</c> del PHP.
/// </summary>
public sealed class IssueGuardTests
{
    private static readonly Error Exhausted = new("plan.limit_reached", "La empresa ha llegado al limite...", ErrorType.Forbidden);

    [TestCase("invoice")]
    [TestCase("credit-note")]
    [TestCase("debit-note")]
    public async Task A_plan_refusal_stops_the_issue_before_taking_a_number(string kind)
    {
        using var harness = IssueHarness.Create();
        harness.Plans.Refusal = Exhausted;

        var result = await harness.IssueAsync(kind, IssueHarness.GoldenInput(CaseOf(kind)));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Is.EqualTo(Exhausted));
            Assert.That(harness.Plans.Checks, Is.EqualTo(new[] { (harness.CompanyId, DocumentKind.Invoice) }));
            Assert.That(harness.Db.Graphs, Is.Empty);
            Assert.That(harness.Db.Messages, Is.Empty);
            Assert.That(harness.Db.Calls, Is.Empty, "No number is taken or reserved.");
        });
    }

    [Test]
    public async Task An_invoice_within_its_plan_is_issued_after_asking_the_plan()
    {
        using var harness = IssueHarness.Create();

        var result = await harness.IssueAsync("invoice", IssueHarness.GoldenInput(IssueHarness.FirstInvoice));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, Describe(result));
            Assert.That(harness.Plans.Checks, Is.EqualTo(new[] { (harness.CompanyId, DocumentKind.Invoice) }));
            Assert.That(harness.SingleGraph.Document.Status, Is.EqualTo(DocumentStatus.Queued));
        });
    }

    [Test]
    public async Task The_test_set_route_does_not_ask_the_plan()
    {
        using var harness = IssueHarness.Create();
        harness.Plans.Refusal = Exhausted;

        var result = await harness.IssueAsync("invoice", IssueHarness.GoldenInput(IssueHarness.FirstInvoice), testSetId: "test-set-1");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, Describe(result));
            Assert.That(harness.Plans.Checks, Is.Empty);
        });
    }

    [TestCase("credit-note")]
    [TestCase("debit-note")]
    public async Task A_note_whose_company_invoice_is_not_accepted_yet_is_rejected_before_taking_a_number(string kind)
    {
        using var harness = IssueHarness.Create();
        var invoice = await harness.IssueAsync("invoice", IssueHarness.GoldenInput(IssueHarness.FirstInvoice));
        harness.Db.Calls.Clear();

        var note = await harness.IssueAsync(kind, ReferencingNote(kind, invoice.Value.Cufe!));

        Assert.Multiple(() =>
        {
            Assert.That(note.IsSuccess, Is.False);
            Assert.That(note.Error.Code, Is.EqualTo("document.reference_not_accepted"));
            Assert.That(note.Error.Type, Is.EqualTo(ErrorType.Validation));
            Assert.That(note.Error.Message, Is.EqualTo(IssueErrors.ReferenceNotAcceptedMessage));
            Assert.That(note.Error.Fields, Is.EqualTo(new[] { new FieldError("billing_reference.uuid", IssueErrors.ReferenceNotAcceptedMessage) }));
            Assert.That(harness.Db.Graphs, Has.Count.EqualTo(1), "Only the invoice was stored.");
            Assert.That(harness.Db.Calls, Is.Empty, "The note takes no number.");
        });
    }

    [TestCase(DocumentStatus.Accepted)]
    [TestCase(DocumentStatus.AcceptedWithWarnings)]
    public async Task A_note_whose_company_invoice_is_accepted_is_issued_and_linked(DocumentStatus accepted)
    {
        using var harness = IssueHarness.Create();
        var invoice = await harness.IssueAsync("invoice", IssueHarness.GoldenInput(IssueHarness.FirstInvoice));
        Accept(harness.Db.Graphs[0].Document, accepted);

        var note = await harness.IssueAsync("credit-note", ReferencingNote("credit-note", invoice.Value.Cufe!));

        Assert.Multiple(() =>
        {
            Assert.That(note.IsSuccess, Is.True, Describe(note));
            Assert.That(harness.Db.Graphs[1].Document.ReferenceDocumentId, Is.EqualTo(invoice.Value.Document.Id));
        });
    }

    [TestCase(DocumentStatus.Rejected)]
    [TestCase(DocumentStatus.Unconfirmed)]
    public async Task A_note_whose_company_invoice_was_rejected_or_is_unconfirmed_is_rejected(DocumentStatus status)
    {
        using var harness = IssueHarness.Create();
        var invoice = await harness.IssueAsync("invoice", IssueHarness.GoldenInput(IssueHarness.FirstInvoice));
        var document = harness.Db.Graphs[0].Document;
        document.MoveTo(DocumentStatus.Sent);
        document.MoveTo(status);

        var note = await harness.IssueAsync("credit-note", ReferencingNote("credit-note", invoice.Value.Cufe!));

        Assert.That(note.IsSuccess ? null : note.Error.Code, Is.EqualTo("document.reference_not_accepted"));
    }

    // La factura de otra empresa o emitida fuera de ImpiFact no está en la base: como en el PHP, la nota se emite sin enlace.
    [Test]
    public async Task A_note_referencing_an_invoice_unknown_to_the_company_is_issued_without_link()
    {
        using var harness = IssueHarness.Create();

        var note = await harness.IssueAsync("credit-note", IssueHarness.GoldenInput(IssueHarness.FirstCreditNote));

        Assert.Multiple(() =>
        {
            Assert.That(note.IsSuccess, Is.True, Describe(note));
            Assert.That(harness.SingleGraph.Document.ReferenceDocumentId, Is.Null);
        });
    }

    [Test]
    public async Task A_test_set_note_does_not_wait_for_its_invoice_to_be_accepted()
    {
        using var harness = IssueHarness.Create();
        var invoice = await harness.IssueAsync("invoice", IssueHarness.GoldenInput(IssueHarness.FirstInvoice), testSetId: "test-set-1");

        var note = await harness.IssueAsync("credit-note", ReferencingNote("credit-note", invoice.Value.Cufe!), testSetId: "test-set-1");

        Assert.Multiple(() =>
        {
            Assert.That(note.IsSuccess, Is.True, Describe(note));
            Assert.That(harness.Db.Graphs[1].Document.ReferenceDocumentId, Is.EqualTo(invoice.Value.Document.Id));
        });
    }

    private static string CaseOf(string kind) => kind switch
    {
        "invoice" => IssueHarness.FirstInvoice,
        "credit-note" => IssueHarness.FirstCreditNote,
        _ => IssueHarness.FirstDebitNote,
    };

    private static byte[] ReferencingNote(string kind, string cufe) =>
        IssueHarness.Edited(CaseOf(kind), json => json["billing_reference"]!["uuid"] = cufe);

    private static void Accept(Document document, DocumentStatus accepted)
    {
        document.MoveTo(DocumentStatus.Sent);
        document.MoveTo(accepted);
    }

    private static string Describe(Result<IssueDocumentResponse> result) =>
        result.IsSuccess ? "" : $"{result.Error.Code}: {result.Error.Message} {string.Join(" | ", result.Error.Fields)}";
}
