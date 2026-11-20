using System.Diagnostics;
using System.Text.Json.Nodes;
using ImpiFact.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ImpiFact.Integration.Tests.Documents;

[TestFixture]
[Category("Integration")]
public sealed class MySqlSaveChangesIsolationTests
{
    [Test]
    public async Task On_mysql_the_implicit_save_changes_transaction_runs_read_committed()
    {
        Assume.That(TestDatabase.IsMySql, "MySqlConnector opens REPEATABLE READ by default only on MySQL.");
        await using var session = DocumentsDatabase.Open(companyId: null);
        var db = session.Db;
        db.Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
        var seen = new List<string>();
        using var subscription = DiagnosticListener.AllListeners.Subscribe(new ListenerObserver(db, seen));
        for (var i = 0; i < 3; i++)
        {
            db.Add(new OutboxMessage { Type = "Test.Isolation", Payload = new JsonObject(), Status = OutboxStatus.Pending, NextAttemptAt = DocumentsDatabase.Clock.UtcNow });
            db.Add(new OutboxMessage { Type = "Test.Isolation", Payload = new JsonObject(), Status = OutboxStatus.Pending, NextAttemptAt = DocumentsDatabase.Clock.UtcNow });
            await db.SaveChangesAsync();
        }

        Assert.That(seen, Is.Not.Empty.And.All.EqualTo("READ-COMMITTED"));
    }

    private sealed class ListenerObserver(DbContext db, List<string> seen) : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == DbLoggerCategory.Name)
            {
                value.Subscribe(this);
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key == RelationalEventId.CommandExecuting.Name && value.Value is CommandEventData data && ReferenceEquals(data.Context, db)
                && data.Command.Transaction is { } tx && data.Command.CommandText.Contains("INSERT", StringComparison.Ordinal))
            {
                using var probe = data.Command.Connection!.CreateCommand();
                probe.Transaction = tx;
                probe.CommandText = "select @@transaction_isolation";
                lock (seen)
                {
                    seen.Add((string)probe.ExecuteScalar()!);
                }
            }
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }
}
