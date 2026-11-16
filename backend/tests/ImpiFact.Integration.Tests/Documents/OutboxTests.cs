using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Integration.Tests.Documents;

/// <summary>
/// La bandeja de salida escribe <c>outbox_messages</c> en la misma transacción de la solicitud (Arquitectura
/// detallada, sección 4.5): el mensaje queda si la transacción confirma y desaparece con ella si se revierte.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class OutboxTests
{
    [Test]
    public async Task A_message_enqueued_in_a_committed_transaction_is_stored_pending_with_its_payload()
    {
        var companyId = await DocumentsDatabase.CreateCompanyAsync();
        var documentId = Guid.CreateVersion7();

        Guid id;
        await using (var session = DocumentsDatabase.Open(companyId))
        {
            await using var transaction = await session.UnitOfWork.BeginTransactionAsync(CancellationToken.None);
            id = await session.Get<IOutbox>().EnqueueAsync(
                OutboxMessageTypes.SendDocumentToDian, new { DocumentId = documentId, TestSetId = "abc" }, companyId, null, CancellationToken.None);
            await session.UnitOfWork.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }

        var message = await FindAsync(id);
        Assert.Multiple(() =>
        {
            Assert.That(message, Is.Not.Null);
            Assert.That(message!.Type, Is.EqualTo("SendDocumentToDian"));
            Assert.That(message.CompanyId, Is.EqualTo(companyId));
            Assert.That(message.Status, Is.EqualTo(OutboxStatus.Pending));
            Assert.That(message.Attempts, Is.Zero);
            Assert.That(message.NextAttemptAt, Is.EqualTo(DocumentsDatabase.Clock.UtcNow));
            Assert.That(message.CreatedAt, Is.EqualTo(DocumentsDatabase.Clock.UtcNow));
            Assert.That(message.Payload["document_id"]!.GetValue<Guid>(), Is.EqualTo(documentId));
            Assert.That(message.Payload["test_set_id"]!.GetValue<string>(), Is.EqualTo("abc"));
        });
    }

    [Test]
    public async Task A_message_can_wait_until_a_given_moment()
    {
        var companyId = await DocumentsDatabase.CreateCompanyAsync();
        var notBefore = DocumentsDatabase.Clock.UtcNow.AddMinutes(10);

        var id = await EnqueueAndCommitAsync(companyId, notBefore);

        Assert.That((await FindAsync(id))!.NextAttemptAt, Is.EqualTo(notBefore));
    }

    [Test]
    public async Task A_message_enqueued_in_a_rolled_back_transaction_is_not_stored()
    {
        var companyId = await DocumentsDatabase.CreateCompanyAsync();

        Guid id;
        await using (var session = DocumentsDatabase.Open(companyId))
        {
            await using var transaction = await session.UnitOfWork.BeginTransactionAsync(CancellationToken.None);
            id = await session.Get<IOutbox>().EnqueueAsync(
                OutboxMessageTypes.SendDocumentToDian, new { Value = 1 }, companyId, null, CancellationToken.None);
            await session.UnitOfWork.SaveChangesAsync(CancellationToken.None);
            await transaction.RollbackAsync(CancellationToken.None);
        }

        Assert.That(await FindAsync(id), Is.Null);
    }

    [Test]
    public async Task A_company_request_cannot_enqueue_messages_for_another_company_or_the_platform()
    {
        var own = await DocumentsDatabase.CreateCompanyAsync();
        await using var session = DocumentsDatabase.Open(own);
        var outbox = session.Get<IOutbox>();

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<InvalidOperationException>(() =>
                outbox.EnqueueAsync(OutboxMessageTypes.SendEmail, new { Value = 1 }, Guid.CreateVersion7(), null, CancellationToken.None));
            Assert.ThrowsAsync<InvalidOperationException>(() =>
                outbox.EnqueueAsync(OutboxMessageTypes.SendEmail, new { Value = 1 }, null, null, CancellationToken.None));
        });
        Assert.That(session.Db.ChangeTracker.Entries<OutboxMessage>(), Is.Empty);
    }

    [Test]
    public async Task A_system_process_can_enqueue_platform_messages_without_a_company()
    {
        var id = await EnqueueAndCommitAsync(companyId: null, notBefore: null);

        Assert.That((await FindAsync(id))!.CompanyId, Is.Null);
    }

    private static async Task<Guid> EnqueueAndCommitAsync(Guid? companyId, DateTimeOffset? notBefore)
    {
        await using var session = DocumentsDatabase.Open(companyId);
        await using var transaction = await session.UnitOfWork.BeginTransactionAsync(CancellationToken.None);
        var id = await session.Get<IOutbox>().EnqueueAsync(
            OutboxMessageTypes.PollDianStatus, new { Value = 1 }, companyId, notBefore, CancellationToken.None);
        await session.UnitOfWork.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return id;
    }

    private static async Task<OutboxMessage?> FindAsync(Guid id)
    {
        await using var session = DocumentsDatabase.Open(companyId: null);
        return await session.Db.Set<OutboxMessage>().AsNoTracking().SingleOrDefaultAsync(message => message.Id == id);
    }
}
