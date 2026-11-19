using System.Data.Common;
using System.Text.Json.Nodes;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Billing;
using ImpiFact.Infrastructure.Documents;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Platform.Outbox;
using ImpiFact.Infrastructure.Platform.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using IDatabaseProvider = ImpiFact.Infrastructure.Persistence.IDatabaseProvider;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Model;

/// <summary>
/// El SQL propio de cada motor (Modelo de datos detallado, secciones 5 y 8): consecutivo, toma de la bandeja de salida,
/// consumo del plan, bloqueo de tareas programadas y la violación de un índice único. Corre igual en cada motor
/// (<c>Database__Provider</c>) contra sus migraciones reales y prueba lo que importa bajo concurrencia: varias sesiones a
/// la vez nunca obtienen el mismo número, nunca toman el mismo mensaje, nunca pierden una cuenta y nunca tienen el mismo
/// bloqueo.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class EngineSqlTests
{
    private const int Parallel = 16;

    [Test]
    public async Task Parallel_takers_on_separate_sessions_get_distinct_consecutive_numbers()
    {
        var graph = await ModelGraph.CreateAsync();
        var scope = await ScopeKeyAsync(graph.CompanyId);

        var numbers = await Task.WhenAll(Enumerable.Range(0, Parallel).Select(_ => InTransactionAsync((session, transaction, numbering) =>
            numbering.TakeNextAsync(session.Db.Database.GetDbConnection(), transaction, graph.CompanyId, scope, ModelDatabase.Clock.UtcNow, CancellationToken.None))));

        Assert.Multiple(async () =>
        {
            Assert.That(numbers, Is.Unique);
            Assert.That(numbers.Order(), Is.EqualTo(Enumerable.Range(0, Parallel).Select(index => (long?)(990000002 + index))));
            Assert.That(await NextNumberAsync(graph.CompanyId, scope), Is.EqualTo(990000002 + Parallel));
        });
    }

    [Test]
    public async Task Raising_above_never_moves_the_sequence_back_and_returns_the_stored_value()
    {
        var graph = await ModelGraph.CreateAsync();
        var scope = await ScopeKeyAsync(graph.CompanyId);

        var raised = await InTransactionAsync((session, transaction, numbering) =>
            numbering.RaiseAboveAsync(session.Db.Database.GetDbConnection(), transaction, graph.CompanyId, scope, 990000010, ModelDatabase.Clock.UtcNow, CancellationToken.None));
        var kept = await InTransactionAsync((session, transaction, numbering) =>
            numbering.RaiseAboveAsync(session.Db.Database.GetDbConnection(), transaction, graph.CompanyId, scope, 5, ModelDatabase.Clock.UtcNow, CancellationToken.None));
        var missing = await InTransactionAsync((session, transaction, numbering) =>
            numbering.TakeNextAsync(session.Db.Database.GetDbConnection(), transaction, graph.CompanyId, "R:missing", ModelDatabase.Clock.UtcNow, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(raised, Is.EqualTo(990000011));
            Assert.That(kept, Is.EqualTo(990000011));
            Assert.That(missing, Is.Null);
        });
    }

    [Test]
    public async Task Concurrent_claimers_never_take_the_same_message_and_only_take_their_types_oldest_first()
    {
        var type = "EngineSqlTest." + Guid.NewGuid().ToString("N");
        var now = ModelDatabase.Clock.UtcNow;
        var due = Enumerable.Range(0, 30).Select(index => Message(type, OutboxStatus.Pending, now.AddMinutes(-30 + index))).ToList();
        var leaseExpired = Message(type, OutboxStatus.Processing, now.AddHours(-2), lockedUntil: now.AddSeconds(-1));
        var leaseAlive = Message(type, OutboxStatus.Processing, now.AddHours(-2), lockedUntil: now.AddMinutes(5));
        var notYet = Message(type, OutboxStatus.Pending, now.AddMinutes(1));
        var otherType = Message(type + ".other", OutboxStatus.Pending, now.AddHours(-3));
        await SaveAsync([.. due, leaseExpired, leaseAlive, notYet, otherType]);

        var claims = await Task.WhenAll(Enumerable.Range(0, 4).Select(index => ClaimAsync($"worker-{index}", type, batchSize: 10)));

        var claimed = claims.SelectMany(claim => claim).ToList();
        var expected = due.Select(message => message.Id).Append(leaseExpired.Id);
        Assert.Multiple(() =>
        {
            Assert.That(claimed.Select(message => message.Id), Is.Unique, "No message is claimed twice.");
            Assert.That(claimed.Select(message => message.Id), Is.EquivalentTo(expected));
            Assert.That(claimed.Select(message => message.Attempts), Has.All.EqualTo(1));
            Assert.That(claimed.Select(message => message.Type), Has.All.EqualTo(type));
            foreach (var claim in claims)
            {
                Assert.That(claim.Select(message => message.NextAttemptAt), Is.Ordered);
            }
        });
    }

    [Test]
    public async Task A_claim_marks_the_messages_as_processing_for_the_worker_until_the_lease_ends()
    {
        var type = "EngineSqlTest." + Guid.NewGuid().ToString("N");
        var now = ModelDatabase.Clock.UtcNow;
        var oldest = Message(type, OutboxStatus.Pending, now.AddMinutes(-3));
        var newer = Message(type, OutboxStatus.Pending, now.AddMinutes(-2));
        await SaveAsync([newer, oldest]);

        var claim = await ClaimAsync("worker-a", type, batchSize: 1);

        await using var session = ModelDatabase.Open(companyId: null);
        var stored = await session.Db.Set<OutboxMessage>().AsNoTracking().SingleAsync(message => message.Id == oldest.Id);
        Assert.Multiple(() =>
        {
            Assert.That(claim.Select(message => message.Id), Is.EqualTo(new[] { oldest.Id }), "The oldest message goes first.");
            Assert.That(stored.Status, Is.EqualTo(OutboxStatus.Processing));
            Assert.That(stored.LockedBy, Is.EqualTo("worker-a"));
            Assert.That(stored.LockedUntil, Is.EqualTo(now.AddMinutes(5)));
            Assert.That(stored.Attempts, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Concurrent_increments_of_a_missing_usage_row_count_every_one()
    {
        var graph = await ModelGraph.CreateAsync();
        Guid companyPlanId;
        await using (var session = ModelDatabase.Open(companyId: null))
        {
            companyPlanId = await session.Db.Set<CompanyPlan>().IgnoreQueryFilters()
                .Where(plan => plan.CompanyId == graph.CompanyId).Select(plan => plan.Id).SingleAsync();
        }

        await Task.WhenAll(Enumerable.Range(0, Parallel).Select(_ => InTransactionAsync(async (session, transaction, _) =>
        {
            await session.Services.GetRequiredService<IPlanUsageSql>().IncrementAsync(
                session.Db.Database.GetDbConnection(), transaction, companyPlanId, DocumentKind.Payroll, CancellationToken.None);
            return 0;
        })));

        await using var check = ModelDatabase.Open(companyId: null);
        var usage = await check.Db.Set<PlanUsage>().AsNoTracking()
            .SingleAsync(row => row.CompanyPlanId == companyPlanId && row.DocumentKind == DocumentKind.Payroll);
        Assert.Multiple(() =>
        {
            Assert.That(usage.Used, Is.EqualTo(Parallel), "No increment is lost and none fails on the duplicate key.");
            Assert.That(usage.RowVersion, Is.EqualTo(Parallel));
        });
    }

    [Test]
    public async Task A_job_lock_belongs_to_one_session_until_it_releases_it()
    {
        var key = Random.Shared.NextInt64();
        await using var first = await OpenSessionAsync();
        await using var second = await OpenSessionAsync();
        var locks = first.Services.GetRequiredService<IScheduledJobLockSql>();
        var firstConnection = first.Db.Database.GetDbConnection();
        var secondConnection = second.Db.Database.GetDbConnection();

        var firstTakes = await locks.TryLockAsync(firstConnection, key, CancellationToken.None);
        var secondWhileHeld = await locks.TryLockAsync(secondConnection, key, CancellationToken.None);
        var parallelAttempts = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var other = await OpenSessionAsync();
            return await locks.TryLockAsync(other.Db.Database.GetDbConnection(), key, CancellationToken.None);
        }));
        await locks.UnlockAsync(firstConnection, key, CancellationToken.None);
        var secondAfterRelease = await locks.TryLockAsync(secondConnection, key, CancellationToken.None);
        var firstWhileSecondHolds = await locks.TryLockAsync(firstConnection, key, CancellationToken.None);
        await locks.UnlockAsync(secondConnection, key, CancellationToken.None);
        // Soltar un bloqueo que la sesión ya no tiene no falla, como en PostgreSQL.
        Assert.DoesNotThrowAsync(() => locks.UnlockAsync(secondConnection, key, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(firstTakes, Is.True);
            Assert.That(secondWhileHeld, Is.False);
            Assert.That(parallelAttempts, Has.All.False);
            Assert.That(secondAfterRelease, Is.True);
            Assert.That(firstWhileSecondHolds, Is.False);
        });
    }

    [Test]
    public async Task Concurrent_sessions_racing_for_a_free_job_lock_get_it_once()
    {
        var key = Random.Shared.NextInt64();
        var sessions = await Task.WhenAll(Enumerable.Range(0, Parallel).Select(_ => OpenSessionAsync()));
        try
        {
            var locks = sessions[0].Services.GetRequiredService<IScheduledJobLockSql>();

            var taken = await Task.WhenAll(sessions.Select(session =>
                locks.TryLockAsync(session.Db.Database.GetDbConnection(), key, CancellationToken.None)));

            Assert.That(taken.Count(result => result), Is.EqualTo(1));
        }
        finally
        {
            foreach (var session in sessions)
            {
                await session.DisposeAsync();
            }
        }
    }

    [Test]
    public async Task A_repeated_unique_key_is_a_unique_violation_and_a_missing_parent_is_not()
    {
        var graph = await ModelGraph.CreateAsync();
        var scope = await ScopeKeyAsync(graph.CompanyId);
        await using var session = ModelDatabase.Open(companyId: null);
        var provider = session.Services.GetRequiredService<IDatabaseProvider>();

        session.Db.Add(new NumberingSequence { CompanyId = graph.CompanyId, ScopeKey = scope, NextNumber = 1 });
        var repeated = Assert.ThrowsAsync<DbUpdateException>(() => session.Db.SaveChangesAsync());
        session.Db.ChangeTracker.Clear();
        session.Db.Add(new NumberingSequence { CompanyId = Guid.CreateVersion7(), ScopeKey = scope, NextNumber = 1 });
        var orphan = Assert.ThrowsAsync<DbUpdateException>(() => session.Db.SaveChangesAsync());

        Assert.Multiple(() =>
        {
            Assert.That(provider.IsUniqueViolation(repeated!), Is.True, repeated?.InnerException?.Message);
            Assert.That(TestDatabase.UniqueViolationName(repeated), Is.EqualTo("ux_numbering_sequences_company_id_scope_key"));
            Assert.That(provider.IsUniqueViolation(orphan!), Is.False, orphan?.InnerException?.Message);
            Assert.That(TestDatabase.IsForeignKeyViolation(orphan), Is.True, orphan?.InnerException?.Message);
        });
    }

    private static OutboxMessage Message(string type, OutboxStatus status, DateTimeOffset nextAttemptAt, DateTimeOffset? lockedUntil = null) => new()
    {
        Type = type,
        Payload = JsonNode.Parse("""{"n":1}""")!,
        Status = status,
        NextAttemptAt = nextAttemptAt,
        LockedBy = lockedUntil is null ? null : "crashed-worker",
        LockedUntil = lockedUntil,
    };

    private static async Task SaveAsync(IEnumerable<object> rows)
    {
        await using var session = ModelDatabase.Open(companyId: null);
        session.Db.AddRange(rows);
        await session.Db.SaveChangesAsync();
    }

    private static async Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimAsync(string worker, string type, int batchSize)
    {
        await using var session = await OpenSessionAsync();
        var now = ModelDatabase.Clock.UtcNow;
        return await session.Services.GetRequiredService<IOutboxQueueSql>().ClaimAsync(
            session.Db.Database.GetDbConnection(), new OutboxClaim(worker, [type], batchSize, now, now.AddMinutes(5)), CancellationToken.None);
    }

    /// <summary>Corre <paramref name="work"/> en su propia sesión y transacción, y la confirma.</summary>
    private static async Task<T> InTransactionAsync<T>(Func<ModelSession, DbTransaction, INumberingSql, Task<T>> work)
    {
        await using var session = ModelDatabase.Open(companyId: null);
        await using var transaction = await session.Db.Database.BeginTransactionAsync();
        var result = await work(session, transaction.GetDbTransaction(), session.Services.GetRequiredService<INumberingSql>());
        await transaction.CommitAsync();
        return result;
    }

    private static async Task<ModelSession> OpenSessionAsync()
    {
        var session = ModelDatabase.Open(companyId: null);
        await session.Db.Database.OpenConnectionAsync();
        return session;
    }

    private static async Task<string> ScopeKeyAsync(Guid companyId)
    {
        await using var session = ModelDatabase.Open(companyId: null);
        return await session.Db.Set<NumberingSequence>().IgnoreQueryFilters()
            .Where(sequence => sequence.CompanyId == companyId).Select(sequence => sequence.ScopeKey).SingleAsync();
    }

    private static async Task<long> NextNumberAsync(Guid companyId, string scope)
    {
        await using var session = ModelDatabase.Open(companyId: null);
        return await session.Db.Set<NumberingSequence>().IgnoreQueryFilters().AsNoTracking()
            .Where(sequence => sequence.CompanyId == companyId && sequence.ScopeKey == scope).Select(sequence => sequence.NextNumber).SingleAsync();
    }
}
