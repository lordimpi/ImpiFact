using ImpiFact.Domain.Documents;

namespace ImpiFact.Domain.Tests.Documents;

/// <summary>
/// Estados del documento y sus transiciones permitidas (Modelo de datos detallado, sección 4). Cualquier
/// transición fuera de la tabla es un error de dominio.
/// </summary>
public sealed class DocumentStatusTests
{
    private static readonly (DocumentStatus From, DocumentStatus To)[] Allowed =
    [
        (DocumentStatus.Received, DocumentStatus.Queued),
        (DocumentStatus.Received, DocumentStatus.ContingencyPending),
        (DocumentStatus.Received, DocumentStatus.Failed),
        (DocumentStatus.Queued, DocumentStatus.Sent),
        (DocumentStatus.Queued, DocumentStatus.Failed),
        (DocumentStatus.ContingencyPending, DocumentStatus.Sent),
        (DocumentStatus.Sent, DocumentStatus.Accepted),
        (DocumentStatus.Sent, DocumentStatus.AcceptedWithWarnings),
        (DocumentStatus.Sent, DocumentStatus.Rejected),
        (DocumentStatus.Sent, DocumentStatus.Unconfirmed),
        (DocumentStatus.Unconfirmed, DocumentStatus.Accepted),
        (DocumentStatus.Unconfirmed, DocumentStatus.AcceptedWithWarnings),
        (DocumentStatus.Unconfirmed, DocumentStatus.Rejected),
        (DocumentStatus.Unconfirmed, DocumentStatus.Failed),
    ];

    public static IEnumerable<TestCaseData> AllowedTransitions() =>
        Allowed.Select(pair => new TestCaseData(pair.From, pair.To).SetArgDisplayNames($"{pair.From}->{pair.To}"));

    public static IEnumerable<TestCaseData> ForbiddenTransitions() =>
        from source in Enum.GetValues<DocumentStatus>()
        from target in Enum.GetValues<DocumentStatus>()
        where !Allowed.Contains((source, target))
        select new TestCaseData(source, target).SetArgDisplayNames($"{source}->{target}");

    [Test]
    public void A_new_document_starts_received()
    {
        Assert.That(NewDocument().Status, Is.EqualTo(DocumentStatus.Received));
    }

    [TestCaseSource(nameof(AllowedTransitions))]
    public void An_allowed_transition_changes_the_status(DocumentStatus from, DocumentStatus to)
    {
        var document = NewDocument(from);

        document.MoveTo(to);

        Assert.Multiple(() =>
        {
            Assert.That(document.Status, Is.EqualTo(to));
            Assert.That(DocumentStatusTransitions.IsAllowed(from, to), Is.True);
        });
    }

    [TestCaseSource(nameof(ForbiddenTransitions))]
    public void Any_other_transition_is_a_domain_error_and_keeps_the_status(DocumentStatus from, DocumentStatus to)
    {
        var document = NewDocument(from);

        var exception = Assert.Throws<InvalidDocumentTransitionException>(() => document.MoveTo(to));

        Assert.Multiple(() =>
        {
            Assert.That(document.Status, Is.EqualTo(from));
            Assert.That(DocumentStatusTransitions.IsAllowed(from, to), Is.False);
            Assert.That(exception!.From, Is.EqualTo(from));
            Assert.That(exception.To, Is.EqualTo(to));
            Assert.That(exception.Message, Does.Contain(from.ToString()).And.Contain(to.ToString()));
        });
    }

    [Test]
    public void Draft_has_no_transitions_because_it_is_never_stored()
    {
        Assert.That(DocumentStatusTransitions.Next(DocumentStatus.Draft), Is.Empty);
    }

    [Test]
    public void An_undefined_status_cannot_be_set()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NewDocument((DocumentStatus)99));
    }

    private static Document NewDocument(DocumentStatus? status = null)
    {
        var snapshot = new CustomerSnapshot { IdentificationNumber = "222222222222", Name = "Consumidor final" };
        return status is { } initial
            ? new Document { FullNumber = "SETP1", CustomerSnapshot = snapshot, Status = initial }
            : new Document { FullNumber = "SETP1", CustomerSnapshot = snapshot };
    }
}
