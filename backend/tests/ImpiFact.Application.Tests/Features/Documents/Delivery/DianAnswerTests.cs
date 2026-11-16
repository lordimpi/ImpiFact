using ImpiFact.Application.Abstractions.Dian;
using ImpiFact.Application.Features.Documents.Delivery;
using ImpiFact.Dian.Soap;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Tests.Features.Documents.Delivery;

/// <summary>
/// Cómo se lee una llamada a la DIAN (la autoridad tributaria de Colombia) para decidir el estado del documento
/// (Arquitectura detallada, sección 7, pasos 8 y 10; supuesto C4 de la fase 4).
/// </summary>
public sealed class DianAnswerTests
{
    private const string Notification = "Regla: FAK55, Notificación: Correo electrónico no informado";
    private const string Rejection = "Regla: FAD06, Rechazo: Valor del CUFE no está calculado correctamente.";
    private const string Rule90 = "Regla: 90, Rechazo: Documento procesado anteriormente.";

    [Test]
    public void A_valid_result_with_status_00_and_no_notifications_is_accepted()
    {
        var answer = DianAnswer.From(Answered("SendBillSync", Result(true, "00")));

        Assert.That(answer, Is.EqualTo(new DianAnswer.Decided(DocumentStatus.Accepted, Result(true, "00"))).Using(DecidedStatus));
    }

    [Test]
    public void A_valid_result_with_notifications_is_accepted_with_warnings()
    {
        var answer = DianAnswer.From(Answered("SendBillSync", Result(true, "00", Notification)));

        Assert.That(((DianAnswer.Decided)answer).Status, Is.EqualTo(DocumentStatus.AcceptedWithWarnings));
    }

    [Test]
    public void An_invalid_result_with_rejection_rules_is_rejected()
    {
        var answer = DianAnswer.From(Answered("SendBillSync", Result(false, "99", Rejection)));

        Assert.That(((DianAnswer.Decided)answer).Status, Is.EqualTo(DocumentStatus.Rejected));
    }

    [Test]
    public void An_invalid_result_with_status_99_and_no_rules_is_rejected()
    {
        var answer = DianAnswer.From(Answered("SendBillSync", Result(false, "99")));

        Assert.That(((DianAnswer.Decided)answer).Status, Is.EqualTo(DocumentStatus.Rejected));
    }

    [TestCase(Rule90)]
    [TestCase("Regla: 90, Rechazo: Documento con CUFE ya existe")]
    [TestCase("regla:90 rechazo")]
    public void Rule_90_means_the_document_was_already_processed(string message)
    {
        var answer = DianAnswer.From(Answered("SendBillSync", Result(false, "99", Rejection, message)));

        Assert.That(answer, Is.InstanceOf<DianAnswer.AlreadyProcessed>());
    }

    [Test]
    public void Rule_900_is_not_rule_90()
    {
        var answer = DianAnswer.From(Answered("SendBillSync", Result(false, "99", "Regla: 900, Rechazo: otra regla")));

        Assert.That(((DianAnswer.Decided)answer).Status, Is.EqualTo(DocumentStatus.Rejected));
    }

    [Test]
    public void Status_98_is_still_in_process()
    {
        var answer = DianAnswer.From(Answered("GetStatus", Result(false, "98")));

        Assert.That(answer, Is.InstanceOf<DianAnswer.InProcess>());
    }

    [Test]
    public void An_invalid_result_without_rules_nor_status_99_is_not_a_decision()
    {
        // Por ejemplo, GetStatus de un documento que la DIAN todavía no registra.
        var answer = DianAnswer.From(Answered("GetStatus", Result(false, "66")));

        Assert.That(answer, Is.InstanceOf<DianAnswer.InProcess>());
    }

    [Test]
    public void An_empty_status_list_is_still_in_process()
    {
        var answer = DianAnswer.From(Call(DianCallOutcome.Answered, new DianResultResponse("GetStatusZip", [])));

        Assert.That(answer, Is.InstanceOf<DianAnswer.InProcess>());
    }

    [Test]
    public void A_test_set_answer_with_a_zip_key_means_the_zip_was_received()
    {
        var answer = DianAnswer.From(Answered("SendTestSetAsync", Result(null, null) with { ZipKey = "3f6e1d52-8a3b" }));

        Assert.That(answer, Is.EqualTo(new DianAnswer.ZipReceived("3f6e1d52-8a3b")));
    }

    [Test]
    public void A_test_set_answer_without_a_zip_key_is_a_rejection()
    {
        var answer = DianAnswer.From(Answered("SendTestSetAsync", Result(null, null, "Set de pruebas inexistente o inactivo.")));

        Assert.That(((DianAnswer.Decided)answer).Status, Is.EqualTo(DocumentStatus.Rejected));
    }

    [TestCase("s:Sender")]
    [TestCase("s:Client")]
    public void A_fault_caused_by_the_sender_is_a_refusal(string code)
    {
        var answer = DianAnswer.From(Call(DianCallOutcome.Answered, new DianSoapFaultResponse(code, null, "The creator of this fault did not specify a Reason.")));

        Assert.That(answer, Is.EqualTo(new DianAnswer.Refused("The creator of this fault did not specify a Reason.")));
    }

    [TestCase("s:Receiver")]
    [TestCase("s:Server")]
    public void A_fault_caused_by_the_service_was_not_delivered(string code)
    {
        var answer = DianAnswer.From(Call(DianCallOutcome.Answered, new DianSoapFaultResponse(code, null, "Internal error")));

        Assert.That(answer, Is.InstanceOf<DianAnswer.NotDelivered>());
    }

    [Test]
    public void An_error_page_means_the_service_is_unavailable_and_nothing_was_delivered()
    {
        var answer = DianAnswer.From(Call(DianCallOutcome.Answered, new DianServiceUnavailableResponse("<html/>")));

        Assert.That(answer, Is.InstanceOf<DianAnswer.NotDelivered>());
    }

    [Test]
    public void An_unrecognized_body_counts_as_no_answer()
    {
        var answer = DianAnswer.From(Call(DianCallOutcome.Answered, new DianUnrecognizedResponse("<x/>")));

        Assert.That(answer, Is.InstanceOf<DianAnswer.NoAnswer>());
    }

    [Test]
    public void A_call_that_was_never_sent_was_not_delivered()
    {
        var answer = DianAnswer.From(Call(DianCallOutcome.NotSent, null) with { Failure = "Connection refused" });

        Assert.That(answer, Is.EqualTo(new DianAnswer.NotDelivered("Connection refused")));
    }

    [Test]
    public void A_call_sent_without_answer_is_no_answer()
    {
        var answer = DianAnswer.From(Call(DianCallOutcome.NoAnswer, null) with { Failure = "Timed out" });

        Assert.That(answer, Is.EqualTo(new DianAnswer.NoAnswer("Timed out")));
    }

    [Test]
    public void An_error_page_after_an_attempt_that_may_have_arrived_is_no_answer()
    {
        var answer = DianAnswer.From(Call(DianCallOutcome.Answered, new DianServiceUnavailableResponse("<html/>")) with { PossiblyReceived = true });

        Assert.That(answer, Is.InstanceOf<DianAnswer.NoAnswer>());
    }

    [TestCase("s:Client")]
    [TestCase("s:Receiver")]
    public void A_fault_after_an_attempt_that_may_have_arrived_is_no_answer(string code)
    {
        var answer = DianAnswer.From(
            Call(DianCallOutcome.Answered, new DianSoapFaultResponse(code, null, "The creator of this fault did not specify a Reason.")) with
            {
                PossiblyReceived = true,
            });

        Assert.That(answer, Is.InstanceOf<DianAnswer.NoAnswer>());
    }

    [Test]
    public void A_decision_after_an_attempt_that_may_have_arrived_is_still_a_decision()
    {
        var answer = DianAnswer.From(Answered("SendBillSync", Result(true, "00")) with { PossiblyReceived = true });

        Assert.That(((DianAnswer.Decided)answer).Status, Is.EqualTo(DocumentStatus.Accepted));
    }

    private static readonly IEqualityComparer<DianAnswer> DecidedStatus = EqualityComparer<DianAnswer>.Create(
        (left, right) => left is DianAnswer.Decided a && right is DianAnswer.Decided b && a.Status == b.Status);

    private static DianResult Result(bool? isValid, string? statusCode, params string[] errors) =>
        new(isValid, statusCode, "Descripción", null, errors, null, null, null, null);

    private static DianCall Answered(string operation, DianResult result) =>
        Call(DianCallOutcome.Answered, new DianResultResponse(operation, [result]));

    private static DianCall Call(DianCallOutcome outcome, DianSoapResponse? response) =>
        new(outcome, "<envelope/>", response is null ? null : 200, response is null ? null : "<body/>", response, TimeSpan.FromSeconds(1), null);
}
