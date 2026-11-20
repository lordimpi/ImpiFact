using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Dapper;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Platform.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Model;

// Regression tests from the T5 verifier: the outbox claim drains a backlog exactly once under
// many workers, and on SQL Server the claim applock never outlives a timed-out or cancelled claim.
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class OutboxClaimConcurrencyTests
{
    private const string ClaimLock = "impifact:outbox-claim";

    [Test]
    public async Task Concurrent_workers_drain_every_due_message_exactly_once()
    {
        var type = UniqueType();
        var now = ModelDatabase.Clock.UtcNow;
        var due = Enumerable.Range(0, 120).Select(i => Message(type, now.AddMinutes(-200 + i))).ToList();
        await SaveAsync(due);

        var claimed = new ConcurrentBag<Guid>();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async worker =>
        {
            while (true)
            {
                var batch = await ClaimAsync($"w{worker}", type, 7);
                if (batch.Count == 0)
                {
                    break;
                }

                foreach (var message in batch)
                {
                    claimed.Add(message.Id);
                }
            }
        }));

        Assert.Multiple(() =>
        {
            Assert.That(claimed, Is.Unique);
            Assert.That(claimed, Is.EquivalentTo(due.Select(m => m.Id)));
        });
    }

    [Test]
    public async Task A_claim_blocked_by_the_applock_times_out_empty_without_counting_an_attempt()
    {
        Assume.That(TestDatabase.IsSqlServer, "The claim applock exists only on SQL Server.");
        var type = UniqueType();
        await SaveAsync([Message(type, ModelDatabase.Clock.UtcNow.AddMinutes(-1))]);

        await using var holder = TestDatabase.Connect(ModelDatabase.ConnectionString);
        await holder.OpenAsync();
        await using var tx = await holder.BeginTransactionAsync();
        var taken = await holder.ExecuteScalarAsync<int>(
            "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0; SELECT @r;",
            new { res = ClaimLock }, tx);
        Assert.That(taken, Is.GreaterThanOrEqualTo(0));

        var watch = Stopwatch.StartNew();
        var blocked = await ClaimAsync("w-blocked", type, 5);
        watch.Stop();
        await tx.CommitAsync();
        var after = await ClaimAsync("w-after", type, 5);

        Assert.Multiple(() =>
        {
            Assert.That(blocked, Is.Empty);
            Assert.That(watch.Elapsed, Is.GreaterThan(TimeSpan.FromSeconds(4)).And.LessThan(TimeSpan.FromSeconds(10)));
            Assert.That(after, Has.Count.EqualTo(1));
            Assert.That(after[0].Attempts, Is.EqualTo(1), "A timed-out claim must not count an attempt.");
        });
    }

    [Test]
    public async Task A_cancelled_claim_leaves_no_lock_and_no_open_transaction()
    {
        Assume.That(TestDatabase.IsSqlServer, "The claim applock exists only on SQL Server.");
        var type = UniqueType();
        await SaveAsync([Message(type, ModelDatabase.Clock.UtcNow.AddMinutes(-1))]);

        await using (var holder = TestDatabase.Connect(ModelDatabase.ConnectionString))
        {
            await holder.OpenAsync();
            await using var tx = await holder.BeginTransactionAsync();
            await holder.ExecuteAsync(
                "EXEC sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0;",
                new { res = ClaimLock }, tx);

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
            Assert.That(await CatchAsync(() => ClaimAsync("w-cancel", type, 5, cts.Token)), Is.Not.Null);
            await tx.CommitAsync();
        }

        var watch = Stopwatch.StartNew();
        var after = await ClaimAsync("w-after", type, 5);
        watch.Stop();

        Assert.Multiple(async () =>
        {
            Assert.That(after, Has.Count.EqualTo(1));
            Assert.That(after[0].Attempts, Is.EqualTo(1));
            Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
            Assert.That(await ClaimLockRequestsAsync(), Is.Zero, "No session still holds or waits for the claim applock.");
            Assert.That(await PooledTranCountsAsync(), Has.All.EqualTo(0));
        });
    }

    [Test]
    public async Task Sql_server_text_columns_are_case_sensitive_and_snapshot_reads_are_on()
    {
        Assume.That(TestDatabase.IsSqlServer, "Collation and snapshot isolation are SQL Server settings.");
        await using var raw = TestDatabase.Connect(ModelDatabase.ConnectionString);
        var columns = (await raw.QueryAsync<(string Table, string Column, string? Collation, short MaxLength)>(
            """
            select t.name, c.name, c.collation_name, c.max_length from sys.columns c
            join sys.tables t on t.object_id = c.object_id
            join sys.types ty on ty.user_type_id = c.user_type_id
            where ty.name in ('nvarchar','varchar','nchar','char') and t.name <> '__EFMigrationsHistory'
            """)).ToList();
        var notCaseSensitive = columns
            .Where(c => c.Collation != "Latin1_General_100_CS_AS_SC" && c.MaxLength != -1)
            .Select(c => $"{c.Table}.{c.Column}={c.Collation}");
        var snapshotReads = await raw.ExecuteScalarAsync<bool>(
            "select is_read_committed_snapshot_on from sys.databases where database_id = db_id()");

        Assert.Multiple(() =>
        {
            Assert.That(columns, Is.Not.Empty);
            Assert.That(notCaseSensitive, Is.Empty, "Bounded text columns must compare case- and accent-sensitively, like PostgreSQL.");
            Assert.That(snapshotReads, Is.True);
        });
    }

    [Test]
    public async Task Sql_server_json_columns_reject_invalid_json()
    {
        Assume.That(TestDatabase.IsSqlServer, "PostgreSQL enforces JSON through the jsonb type.");
        var message = Message(UniqueType(), ModelDatabase.Clock.UtcNow);
        await SaveAsync([message]);
        await using var raw = TestDatabase.Connect(ModelDatabase.ConnectionString);

        var error = await CatchAsync(() => raw.ExecuteAsync(
            "update outbox_messages set payload = '{not json' where id = @id", new { id = message.Id }));

        Assert.That(error, Is.Not.Null);
    }

    private static string UniqueType() => "ClaimConcurrency." + Guid.NewGuid().ToString("N");

    private static async Task<Exception?> CatchAsync(Func<Task> work)
    {
        try
        {
            await work();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task<int> ClaimLockRequestsAsync()
    {
        await using var raw = TestDatabase.Connect(ModelDatabase.ConnectionString);
        return await raw.ExecuteScalarAsync<int>(
            "select count(*) from sys.dm_tran_locks where resource_type = 'APPLICATION' and resource_description like '%outbox-cl%'");
    }

    private static async Task<List<int>> PooledTranCountsAsync()
    {
        var counts = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            await using var raw = TestDatabase.Connect(ModelDatabase.ConnectionString);
            counts.Add(await raw.ExecuteScalarAsync<int>("select @@trancount"));
        }

        return counts;
    }

    private static OutboxMessage Message(string type, DateTimeOffset nextAttemptAt) => new()
    {
        Type = type,
        Payload = JsonNode.Parse("""{"n":1}""")!,
        Status = OutboxStatus.Pending,
        NextAttemptAt = nextAttemptAt,
    };

    private static async Task SaveAsync(IEnumerable<object> rows)
    {
        await using var session = ModelDatabase.Open(companyId: null);
        session.Db.AddRange(rows);
        await session.Db.SaveChangesAsync();
    }

    private static async Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimAsync(
        string worker, string type, int batchSize, CancellationToken ct = default)
    {
        await using var session = ModelDatabase.Open(companyId: null);
        await session.Db.Database.OpenConnectionAsync(ct);
        var now = ModelDatabase.Clock.UtcNow;
        return await session.Services.GetRequiredService<IOutboxQueueSql>().ClaimAsync(
            session.Db.Database.GetDbConnection(), new OutboxClaim(worker, [type], batchSize, now, now.AddMinutes(5)), ct);
    }
}
