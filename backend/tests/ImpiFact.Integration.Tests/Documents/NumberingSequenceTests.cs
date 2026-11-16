using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Domain.Documents;
using ImpiFact.Integration.Tests.Model;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Integration.Tests.Documents;

/// <summary>
/// Consecutivos en PostgreSQL (Modelo de datos detallado, sección 5): el número se toma con un solo UPDATE atómico
/// dentro de la transacción de la solicitud, dos solicitudes simultáneas nunca obtienen el mismo número, un número
/// reservado por el cliente nunca hace bajar el consecutivo y cada empresa solo toca sus propios consecutivos.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class NumberingSequenceTests
{
    private static readonly TimeSpan BlockedWindow = TimeSpan.FromMilliseconds(500);

    [Test]
    public async Task Parallel_takers_get_distinct_consecutive_numbers()
    {
        const int takers = 25;
        var (companyId, scopeKey) = await CreateSequenceAsync(next: 1);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, takers).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            return await TakeAndCommitAsync(companyId, scopeKey);
        })).ToList();
        start.SetResult();
        var numbers = await Task.WhenAll(tasks);

        var sequence = await ReadSequenceAsync(companyId, scopeKey);
        Assert.Multiple(() =>
        {
            Assert.That(numbers.Order(), Is.EqualTo(Enumerable.Range(1, takers).Select(number => (long?)number)));
            Assert.That(sequence.NextNumber, Is.EqualTo(takers + 1));
            Assert.That(sequence.RowVersion, Is.EqualTo(1 + takers), "Every take raises row_version like SaveChanges would.");
        });
    }

    [Test]
    public async Task A_second_taker_waits_for_the_first_transaction_and_then_gets_the_next_number()
    {
        var (companyId, scopeKey) = await CreateSequenceAsync(next: 10);

        await using var first = DocumentsDatabase.Open(companyId);
        await using var firstTransaction = await first.UnitOfWork.BeginTransactionAsync(CancellationToken.None);
        var firstNumber = await first.Get<INumberingSequences>().TakeNextAsync(companyId, scopeKey, CancellationToken.None);

        var second = Task.Run(() => TakeAndCommitAsync(companyId, scopeKey));
        var blocked = await Task.WhenAny(second, Task.Delay(BlockedWindow)) != second;
        await firstTransaction.CommitAsync(CancellationToken.None);
        var secondNumber = await second;

        Assert.Multiple(() =>
        {
            Assert.That(blocked, Is.True, "The UPDATE keeps the row locked until the first transaction ends.");
            Assert.That(firstNumber, Is.EqualTo(10));
            Assert.That(secondNumber, Is.EqualTo(11));
        });
    }

    [Test]
    public async Task A_number_taken_in_a_rolled_back_transaction_is_taken_again()
    {
        var (companyId, scopeKey) = await CreateSequenceAsync(next: 5);

        long? rolledBack;
        await using (var session = DocumentsDatabase.Open(companyId))
        {
            await using var transaction = await session.UnitOfWork.BeginTransactionAsync(CancellationToken.None);
            rolledBack = await session.Get<INumberingSequences>().TakeNextAsync(companyId, scopeKey, CancellationToken.None);
            await transaction.RollbackAsync(CancellationToken.None);
        }

        var taken = await TakeAndCommitAsync(companyId, scopeKey);

        Assert.Multiple(() =>
        {
            Assert.That(rolledBack, Is.EqualTo(5));
            Assert.That(taken, Is.EqualTo(5));
        });
    }

    [Test]
    public async Task Taking_stamps_updated_at_with_the_application_clock()
    {
        var (companyId, scopeKey) = await CreateSequenceAsync(next: 1);
        var before = await ReadSequenceAsync(companyId, scopeKey);
        DocumentsDatabase.Clock.Advance(TimeSpan.FromMinutes(3));

        await TakeAndCommitAsync(companyId, scopeKey);

        var after = await ReadSequenceAsync(companyId, scopeKey);
        Assert.Multiple(() =>
        {
            Assert.That(after.UpdatedAt, Is.EqualTo(DocumentsDatabase.Clock.UtcNow));
            Assert.That(after.CreatedAt, Is.EqualTo(before.CreatedAt));
        });
    }

    [Test]
    public async Task Taking_from_a_missing_sequence_returns_null()
    {
        var companyId = await DocumentsDatabase.CreateCompanyAsync();

        Assert.That(await TakeAndCommitAsync(companyId, NumberingScope.ForResolution(Guid.CreateVersion7())), Is.Null);
    }

    [Test]
    public async Task Taking_or_reserving_without_a_transaction_is_refused()
    {
        var (companyId, scopeKey) = await CreateSequenceAsync(next: 1);
        await using var session = DocumentsDatabase.Open(companyId);
        var numbering = session.Get<INumberingSequences>();

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<InvalidOperationException>(() => numbering.TakeNextAsync(companyId, scopeKey, CancellationToken.None));
            Assert.ThrowsAsync<InvalidOperationException>(() => numbering.ReserveAsync(companyId, scopeKey, Key(1), CancellationToken.None));
        });
        Assert.That((await ReadSequenceAsync(companyId, scopeKey)).NextNumber, Is.EqualTo(1));
    }

    [Test]
    public async Task Taking_in_one_company_leaves_the_same_scope_of_another_company_untouched()
    {
        var scopeKey = NumberingScope.ForResolution(Guid.CreateVersion7());
        var (first, _) = await CreateSequenceAsync(next: 1, scopeKey);
        var (second, _) = await CreateSequenceAsync(next: 100, scopeKey);

        var taken = await TakeAndCommitAsync(first, scopeKey);
        var firstSequence = await ReadSequenceAsync(first, scopeKey);
        var secondSequence = await ReadSequenceAsync(second, scopeKey);

        Assert.Multiple(() =>
        {
            Assert.That(taken, Is.EqualTo(1));
            Assert.That(firstSequence.NextNumber, Is.EqualTo(2));
            Assert.That(secondSequence.NextNumber, Is.EqualTo(100));
            Assert.That(secondSequence.RowVersion, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task A_company_request_cannot_take_or_reserve_numbers_of_another_company()
    {
        var (other, scopeKey) = await CreateSequenceAsync(next: 1);
        var own = await DocumentsDatabase.CreateCompanyAsync();
        await using var session = DocumentsDatabase.Open(own);
        await using var transaction = await session.UnitOfWork.BeginTransactionAsync(CancellationToken.None);
        var numbering = session.Get<INumberingSequences>();

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<InvalidOperationException>(() => numbering.TakeNextAsync(other, scopeKey, CancellationToken.None));
            Assert.ThrowsAsync<InvalidOperationException>(() => numbering.ReserveAsync(other, scopeKey, Key(5), CancellationToken.None));
        });
        await transaction.CommitAsync(CancellationToken.None);
        Assert.That((await ReadSequenceAsync(other, scopeKey)).NextNumber, Is.EqualTo(1));
    }

    [Test]
    public async Task A_system_process_can_take_numbers_for_any_company()
    {
        var (companyId, scopeKey) = await CreateSequenceAsync(next: 7);

        await using var session = DocumentsDatabase.Open(companyId: null, ActorType.System);
        await using var transaction = await session.UnitOfWork.BeginTransactionAsync(CancellationToken.None);
        var taken = await session.Get<INumberingSequences>().TakeNextAsync(companyId, scopeKey, CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);

        Assert.That(taken, Is.EqualTo(7));
    }

    [Test]
    public async Task Reserving_a_number_at_or_above_the_sequence_moves_it_past_that_number()
    {
        var (companyId, scopeKey) = await CreateSequenceAsync(next: 10);

        var reservation = await ReserveAndCommitAsync(companyId, scopeKey, Key(40));
        var next = await TakeAndCommitAsync(companyId, scopeKey);

        Assert.Multiple(() =>
        {
            Assert.That(reservation, Is.EqualTo(NumberReservation.Reserved));
            Assert.That(next, Is.EqualTo(41), "Automatic numbers never repeat a reserved one.");
        });
    }

    [Test]
    public async Task Reserving_a_free_number_below_the_sequence_never_moves_it_back()
    {
        var (companyId, scopeKey) = await CreateSequenceAsync(next: 50);

        var reservation = await ReserveAndCommitAsync(companyId, scopeKey, Key(20));
        var sequence = await ReadSequenceAsync(companyId, scopeKey);

        Assert.Multiple(() =>
        {
            Assert.That(reservation, Is.EqualTo(NumberReservation.Reserved));
            Assert.That(sequence.NextNumber, Is.EqualTo(50));
        });
    }

    [Test]
    public async Task Reserving_a_number_that_a_stored_document_uses_reports_it_as_used()
    {
        var (companyId, scopeKey) = await CreateSequenceAsync(next: 1);
        await SaveDocumentAsync(companyId, number: 3);

        var reservation = await ReserveAndCommitAsync(companyId, scopeKey, Key(3));

        Assert.That(reservation, Is.EqualTo(NumberReservation.AlreadyUsed));
    }

    [Test]
    public async Task A_document_of_another_company_with_the_same_number_does_not_count_as_used()
    {
        var (companyId, scopeKey) = await CreateSequenceAsync(next: 1);
        await SaveDocumentAsync(await DocumentsDatabase.CreateCompanyAsync(), number: 3);

        Assert.That(await ReserveAndCommitAsync(companyId, scopeKey, Key(3)), Is.EqualTo(NumberReservation.Reserved));
    }

    [Test]
    public async Task Reserving_on_a_missing_sequence_reports_it()
    {
        var companyId = await DocumentsDatabase.CreateCompanyAsync();

        var reservation = await ReserveAndCommitAsync(companyId, NumberingScope.ForResolution(Guid.CreateVersion7()), Key(3));

        Assert.That(reservation, Is.EqualTo(NumberReservation.SequenceNotFound));
    }

    [Test]
    public async Task Two_simultaneous_reservations_of_the_same_number_are_served_one_after_the_other()
    {
        var (companyId, scopeKey) = await CreateSequenceAsync(next: 1);

        await using var first = DocumentsDatabase.Open(companyId);
        await using var firstTransaction = await first.UnitOfWork.BeginTransactionAsync(CancellationToken.None);
        var firstReservation = await first.Get<INumberingSequences>().ReserveAsync(companyId, scopeKey, Key(60), CancellationToken.None);
        first.Db.Add(ModelGraph.NewDocument(companyId, 60, cufe: null));
        await first.UnitOfWork.SaveChangesAsync(CancellationToken.None);

        var second = Task.Run(() => ReserveAndCommitAsync(companyId, scopeKey, Key(60)));
        var blocked = await Task.WhenAny(second, Task.Delay(BlockedWindow)) != second;
        await firstTransaction.CommitAsync(CancellationToken.None);
        var secondReservation = await second;

        Assert.Multiple(() =>
        {
            Assert.That(firstReservation, Is.EqualTo(NumberReservation.Reserved));
            Assert.That(blocked, Is.True, "The second reservation waits on the sequence row lock.");
            Assert.That(secondReservation, Is.EqualTo(NumberReservation.AlreadyUsed));
        });
    }

    private static DocumentNumberKey Key(long number) => new(DocumentTypeId: 1, TypeEnvironmentId: 2, Prefix: "SETP", number);

    private static async Task<(Guid CompanyId, string ScopeKey)> CreateSequenceAsync(long next, string? scopeKey = null)
    {
        var companyId = await DocumentsDatabase.CreateCompanyAsync();
        var key = scopeKey ?? NumberingScope.ForResolution(Guid.CreateVersion7());
        await using var session = DocumentsDatabase.Open(companyId);
        session.Db.Add(new NumberingSequence { CompanyId = companyId, ScopeKey = key, NextNumber = next });
        await session.Db.SaveChangesAsync();
        return (companyId, key);
    }

    private static async Task SaveDocumentAsync(Guid companyId, long number)
    {
        await using var session = DocumentsDatabase.Open(companyId);
        session.Db.Add(ModelGraph.NewDocument(companyId, number, cufe: null));
        await session.Db.SaveChangesAsync();
    }

    private static async Task<long?> TakeAndCommitAsync(Guid companyId, string scopeKey)
    {
        await using var session = DocumentsDatabase.Open(companyId);
        await using var transaction = await session.UnitOfWork.BeginTransactionAsync(CancellationToken.None);
        var number = await session.Get<INumberingSequences>().TakeNextAsync(companyId, scopeKey, CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return number;
    }

    private static async Task<NumberReservation> ReserveAndCommitAsync(Guid companyId, string scopeKey, DocumentNumberKey number)
    {
        await using var session = DocumentsDatabase.Open(companyId);
        await using var transaction = await session.UnitOfWork.BeginTransactionAsync(CancellationToken.None);
        var reservation = await session.Get<INumberingSequences>().ReserveAsync(companyId, scopeKey, number, CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return reservation;
    }

    private static async Task<NumberingSequence> ReadSequenceAsync(Guid companyId, string scopeKey)
    {
        await using var session = DocumentsDatabase.Open(companyId: null);
        return await session.Db.Set<NumberingSequence>().AsNoTracking().IgnoreQueryFilters()
            .SingleAsync(sequence => sequence.CompanyId == companyId && sequence.ScopeKey == scopeKey);
    }
}
