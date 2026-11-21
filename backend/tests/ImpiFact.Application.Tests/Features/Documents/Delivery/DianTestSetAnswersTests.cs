using ImpiFact.Application.Abstractions.Dian;

namespace ImpiFact.Application.Tests.Features.Documents.Delivery;

/// <summary>
/// Respuestas de la DIAN al set de pruebas que se reconocen por su descripción: «ya aceptado» (final) y «en proceso»
/// (todavía sin resultado, aunque lleguen con IsValid falso).
/// </summary>
public sealed class DianTestSetAnswersTests
{
    private const string Accepted = "Set de prueba con identificador 00000000-0000-0000-0000-000000000000 se encuentra Aceptado";
    private const string SetInProcess = "Set de prueba con identificador 00000000-0000-0000-0000-000000000000 se encuentra en proceso";

    [TestCase("Batch en proceso de validación.")]
    [TestCase(SetInProcess)]
    [TestCase("BATCH EN PROCESO DE VALIDACIÓN.")]
    public void An_answer_that_the_dian_is_still_processing_is_in_process_and_not_accepted(string description)
    {
        Assert.Multiple(() =>
        {
            Assert.That(DianTestSetAnswers.IsInProcess(description), Is.True);
            Assert.That(DianTestSetAnswers.IsAlreadyAccepted(description), Is.False);
        });
    }

    [Test]
    public void An_accepted_test_set_is_not_in_process()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DianTestSetAnswers.IsAlreadyAccepted(Accepted), Is.True);
            Assert.That(DianTestSetAnswers.IsInProcess(Accepted), Is.False);
        });
    }

    [TestCase(null)]
    [TestCase("Documento con errores en campos mandatorios.")]
    [TestCase("Procesado Correctamente.")]
    public void Other_answers_are_neither(string? description)
    {
        Assert.Multiple(() =>
        {
            Assert.That(DianTestSetAnswers.IsInProcess(description), Is.False);
            Assert.That(DianTestSetAnswers.IsAlreadyAccepted(description), Is.False);
        });
    }
}
