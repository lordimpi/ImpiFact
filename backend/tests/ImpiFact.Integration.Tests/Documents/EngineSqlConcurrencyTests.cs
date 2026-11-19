using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.Json.Nodes;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Billing;
using ImpiFact.Infrastructure.Platform.Outbox;
using ImpiFact.Infrastructure.Platform.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ImpiFact.Integration.Tests.Documents;

/// <summary>
/// El SQL propio de cada motor (consecutivo, toma del outbox, consumo del plan, bloqueo de tareas y llave repetida) bajo
/// concurrencia real, llamado directamente por sus interfaces. Corre con el motor de <see cref="TestDatabase"/>, así
/// cada proveedor (PostgreSQL, MySQL) prueba las mismas garantías con su propio SQL.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class EngineSqlConcurrencyTests
{
    private static int planSequence = 900_000;

    // Modelo de datos detallado, sección 10, criterio 5: 100 tareas paralelas sin duplicados ni huecos.
    [Test]
    public async Task One_hundred_parallel_takers_get_every_number_exactly_once()
    {
        const int takers = 100;
        var companyId = await DocumentsDatabase.CreateCompanyAsync();
        var scopeKey = NumberingScope.ForResolution(Guid.CreateVersion7());
        await using (var session = DocumentsDatabase.Open(companyId))
        {
            session.Db.Add(new NumberingSequence { CompanyId = companyId, ScopeKey = scopeKey, NextNumber = 1 });
            await session.Db.SaveChangesAsync();
        }

        var numbers = await RunTogetherAsync(takers, async () =>
        {
            await using var session = DocumentsDatabase.Open(companyId);
            await using var transaction = await session.UnitOfWork.BeginTransactionAsync(CancellationToken.None);
            var number = await session.Get<INumberingSequences>().TakeNextAsync(companyId, scopeKey, CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
            return number;
        });

        Assert.That(numbers.Order(), Is.EqualTo(Enumerable.Range(1, takers).Select(number => (long?)number)));
    }

    [Test]
    public async Task Parallel_claimers_split_the_outbox_and_never_take_a_message_twice()
    {
        const int messages = 40;
        var type = "test.concurrency." + Guid.NewGuid().ToString("N");
        var now = DocumentsDatabase.Clock.UtcNow;
        await using (var session = DocumentsDatabase.Open(companyId: null))
        {
            for (var index = 0; index < messages; index++)
            {
                session.Db.Add(new OutboxMessage
                {
                    Type = type, Payload = JsonNode.Parse($$"""{"index":{{index}}}""")!, Status = OutboxStatus.Pending,
                    NextAttemptAt = now.AddSeconds(-messages + index),
                });
            }

            await session.Db.SaveChangesAsync();
        }

        var claimed = new ConcurrentBag<(string Worker, Guid Id, int Attempts)>();
        await RunTogetherAsync(4, async worker =>
        {
            await using var session = DocumentsDatabase.Open(companyId: null);
            await session.Db.Database.OpenConnectionAsync();
            var sql = session.Get<IOutboxQueueSql>();
            while (true)
            {
                var batch = await sql.ClaimAsync(
                    session.Db.Database.GetDbConnection(),
                    new OutboxClaim($"worker-{worker}", [type], BatchSize: 3, now, now.AddMinutes(5)),
                    CancellationToken.None);
                if (batch.Count == 0)
                {
                    return 0;
                }

                Assert.That(batch.Select(message => message.NextAttemptAt), Is.Ordered, "A batch comes oldest first.");
                foreach (var message in batch)
                {
                    claimed.Add(($"worker-{worker}", message.Id, message.Attempts));
                }
            }
        });

        await using var check = DocumentsDatabase.Open(companyId: null);
        var rows = await check.Db.Set<OutboxMessage>().AsNoTracking().Where(message => message.Type == type).ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(claimed.Select(item => item.Id), Is.Unique);
            Assert.That(claimed.Select(item => item.Id), Is.EquivalentTo(rows.Select(row => row.Id)));
            Assert.That(claimed.Select(item => item.Attempts), Is.All.EqualTo(1));
            Assert.That(rows.Select(row => row.Status), Is.All.EqualTo(OutboxStatus.Processing));
            Assert.That(
                rows.Select(row => (row.LockedBy, row.Id)),
                Is.EquivalentTo(claimed.Select(item => ((string?)item.Worker, item.Id))),
                "Each row is locked by the worker that claimed it.");
        });
    }

    // La primera vez la fila no existe: las transacciones compiten por crearla y ninguna cuenta se pierde.
    [Test]
    public async Task Parallel_first_increments_of_a_plan_usage_never_lose_a_count()
    {
        const int increments = 30;
        var companyPlanId = await CreateCompanyPlanAsync();

        await RunTogetherAsync(increments, async () =>
        {
            await using var session = DocumentsDatabase.Open(companyId: null);
            await using var transaction = await session.Db.Database.BeginTransactionAsync();
            await session.Get<IPlanUsageSql>().IncrementAsync(
                session.Db.Database.GetDbConnection(), transaction.GetDbTransaction(), companyPlanId, DocumentKind.Invoice, CancellationToken.None);
            await transaction.CommitAsync();
            return 0;
        });

        await using var check = DocumentsDatabase.Open(companyId: null);
        var usage = await check.Db.Set<PlanUsage>().AsNoTracking().SingleAsync(row => row.CompanyPlanId == companyPlanId);
        Assert.Multiple(() =>
        {
            Assert.That(usage.Used, Is.EqualTo(increments));
            Assert.That(usage.RowVersion, Is.EqualTo(increments));
        });
    }

    [Test]
    public async Task Of_sessions_asking_for_the_same_job_lock_at_once_only_one_gets_it_until_it_lets_go()
    {
        var key = Random.Shared.NextInt64();
        var sessions = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => OpenLockSessionAsync()));
        try
        {
            var sql = sessions[0].Get<IScheduledJobLockSql>();
            var taken = await RunTogetherAsync(sessions.Length, index => sql.TryLockAsync(Connection(sessions[index]), key, CancellationToken.None));
            var holder = Array.IndexOf(taken, true);
            var other = holder == 0 ? 1 : 0;

            Assert.That(taken.Count(result => result), Is.EqualTo(1));
            Assert.That(await sql.TryLockAsync(Connection(sessions[other]), key + 1, CancellationToken.None), Is.True, "Another job is not blocked.");
            await sql.UnlockAsync(Connection(sessions[holder]), key, CancellationToken.None);
            Assert.That(await sql.TryLockAsync(Connection(sessions[other]), key, CancellationToken.None), Is.True);
            Assert.That(await sql.TryLockAsync(Connection(sessions[holder]), key, CancellationToken.None), Is.False, "Now the other one has it.");
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
    public async Task A_repeated_unique_key_is_a_unique_violation_and_a_missing_reference_is_not()
    {
        var companyId = await DocumentsDatabase.CreateCompanyAsync();
        var scopeKey = NumberingScope.ForResolution(Guid.CreateVersion7());
        await using (var session = DocumentsDatabase.Open(companyId: null))
        {
            session.Db.Add(new NumberingSequence { CompanyId = companyId, ScopeKey = scopeKey, NextNumber = 1 });
            await session.Db.SaveChangesAsync();
        }

        var repeated = await SaveFailureAsync(new NumberingSequence { CompanyId = companyId, ScopeKey = scopeKey, NextNumber = 5 });
        var dangling = await SaveFailureAsync(new NumberingSequence { CompanyId = Guid.CreateVersion7(), ScopeKey = scopeKey, NextNumber = 1 });

        await using var check = DocumentsDatabase.Open(companyId: null);
        var provider = check.Get<ImpiFact.Infrastructure.Persistence.IDatabaseProvider>();
        Assert.Multiple(() =>
        {
            Assert.That(provider.IsUniqueViolation(repeated), Is.True, repeated.InnerException?.Message);
            Assert.That(provider.IsUniqueViolation(dangling), Is.False, dangling.InnerException?.Message);
        });
    }

    private static async Task<DbUpdateException> SaveFailureAsync(NumberingSequence sequence)
    {
        await using var session = DocumentsDatabase.Open(companyId: null);
        session.Db.Add(sequence);
        return Assert.ThrowsAsync<DbUpdateException>(() => session.Db.SaveChangesAsync())!;
    }

    private static async Task<Guid> CreateCompanyPlanAsync()
    {
        var companyId = await DocumentsDatabase.CreateCompanyAsync();
        var planId = Interlocked.Increment(ref planSequence);
        await using var session = DocumentsDatabase.Open(companyId: null);
        session.Db.Add(new Plan { Id = planId, Name = $"Concurrency {planId}", Period = PlanPeriod.Package, IsActive = true });
        var companyPlan = new CompanyPlan { CompanyId = companyId, PlanId = planId, StartsAt = DocumentsDatabase.Clock.UtcNow, IsActive = true };
        session.Db.Add(companyPlan);
        await session.Db.SaveChangesAsync();
        return companyPlan.Id;
    }

    private static async Task<DocumentsSession> OpenLockSessionAsync()
    {
        var session = DocumentsDatabase.Open(companyId: null);
        await session.Db.Database.OpenConnectionAsync();
        return session;
    }

    private static DbConnection Connection(DocumentsSession session) => session.Db.Database.GetDbConnection();

    /// <summary>Arranca <paramref name="count"/> tareas a la vez (esperan la misma señal) y junta sus resultados.</summary>
    private static Task<T[]> RunTogetherAsync<T>(int count, Func<Task<T>> work) => RunTogetherAsync(count, _ => work());

    private static async Task<T[]> RunTogetherAsync<T>(int count, Func<int, Task<T>> work)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, count).Select(index => Task.Run(async () =>
        {
            await start.Task;
            return await work(index);
        })).ToList();
        start.SetResult();
        return await Task.WhenAll(tasks);
    }
}
