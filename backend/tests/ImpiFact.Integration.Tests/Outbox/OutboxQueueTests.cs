using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Platform.Outbox;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Outbox;

/// <summary>
/// La cola sobre <c>outbox_messages</c>: la toma con <c>FOR UPDATE SKIP LOCKED</c> (bloquea las filas que toma y
/// salta las que otra transacción ya bloqueó) reparte los mensajes entre instancias sin repetir ninguno, y solo la
/// instancia que tiene el bloqueo puede cerrar el mensaje.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class OutboxQueueTests
{
    [Test]
    public async Task Two_concurrent_claimers_never_take_the_same_message()
    {
        await using var host = OutboxHost.Create();
        var companyId = await host.CreateCompanyAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 40; i++)
        {
            ids.Add(await host.EnqueueAsync(companyId));
        }

        var takenByA = new List<Guid>();
        var takenByB = new List<Guid>();
        for (var round = 0; round < 4; round++)
        {
            var claims = await Task.WhenAll(ClaimAsync(host, "worker-a"), ClaimAsync(host, "worker-b"));
            takenByA.AddRange(claims[0]);
            takenByB.AddRange(claims[1]);
        }

        var messages = await Task.WhenAll(ids.Select(host.FindAsync));
        Assert.Multiple(() =>
        {
            Assert.That(takenByA.Intersect(takenByB), Is.Empty);
            Assert.That(takenByA.Concat(takenByB), Is.Unique);
            Assert.That(takenByA.Concat(takenByB), Is.EquivalentTo(ids));
            Assert.That(messages.Select(message => message.Attempts), Is.All.EqualTo(1));
            Assert.That(messages.Where(message => takenByA.Contains(message.Id)).Select(message => message.LockedBy), Is.All.EqualTo("worker-a"));
            Assert.That(messages.Where(message => takenByB.Contains(message.Id)).Select(message => message.LockedBy), Is.All.EqualTo("worker-b"));
            Assert.That(messages.Select(message => message.Status), Is.All.EqualTo(OutboxStatus.Processing));
            Assert.That(messages.Select(message => message.LockedUntil), Is.All.EqualTo(host.Clock.UtcNow.AddMinutes(5)));
        });
    }

    [Test]
    public async Task A_stale_owner_cannot_close_a_message_that_another_instance_took_again()
    {
        await using var host = OutboxHost.Create();
        var companyId = await host.CreateCompanyAsync();
        var id = await host.EnqueueAsync(companyId);

        await using var staleScope = host.OpenScope();
        var stale = staleScope.ServiceProvider.GetRequiredService<OutboxQueue>();
        var staleClaim = (await stale.ClaimAsync("worker-stale", [host.MessageType], batchSize: 10, CancellationToken.None)).Single();

        host.Clock.Advance(TimeSpan.FromMinutes(6));
        await using var freshScope = host.OpenScope();
        var fresh = freshScope.ServiceProvider.GetRequiredService<OutboxQueue>();
        var freshClaim = (await fresh.ClaimAsync("worker-fresh", [host.MessageType], batchSize: 10, CancellationToken.None)).Single();

        var staleCompleted = await stale.CompleteAsync("worker-stale", staleClaim, CancellationToken.None);
        var staleFailed = await stale.FailAsync("worker-stale", staleClaim, new InvalidOperationException("Late."), CancellationToken.None);
        var afterStale = await host.FindAsync(id);
        var freshCompleted = await fresh.CompleteAsync("worker-fresh", freshClaim, CancellationToken.None);
        var afterFresh = await host.FindAsync(id);

        Assert.Multiple(() =>
        {
            Assert.That(freshClaim.Id, Is.EqualTo(id));
            Assert.That(freshClaim.Attempts, Is.EqualTo(2));
            Assert.That(staleCompleted, Is.False);
            Assert.That(staleFailed, Is.EqualTo(OutboxFailureOutcome.NotOwned));
            Assert.That(afterStale.Status, Is.EqualTo(OutboxStatus.Processing));
            Assert.That(afterStale.LockedBy, Is.EqualTo("worker-fresh"));
            Assert.That(afterStale.LastError, Is.Null);
            Assert.That(freshCompleted, Is.True);
            Assert.That(afterFresh.Status, Is.EqualTo(OutboxStatus.Done));
        });
    }

    [Test]
    public async Task A_message_closed_once_cannot_be_closed_again()
    {
        await using var host = OutboxHost.Create();
        var companyId = await host.CreateCompanyAsync();
        await host.EnqueueAsync(companyId);
        await using var scope = host.OpenScope();
        var queue = scope.ServiceProvider.GetRequiredService<OutboxQueue>();
        var claim = (await queue.ClaimAsync("worker-a", [host.MessageType], batchSize: 10, CancellationToken.None)).Single();

        var first = await queue.CompleteAsync("worker-a", claim, CancellationToken.None);
        var second = await queue.CompleteAsync("worker-a", claim, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.True);
            Assert.That(second, Is.False);
        });
    }

    private static async Task<IReadOnlyList<Guid>> ClaimAsync(OutboxHost host, string workerId)
    {
        await using var scope = host.OpenScope();
        var claimed = await scope.ServiceProvider.GetRequiredService<OutboxQueue>()
            .ClaimAsync(workerId, [host.MessageType], batchSize: 7, CancellationToken.None);
        return claimed.Select(message => message.Id).ToList();
    }

    // La misma instancia vuelve a tomar su propio mensaje después de que venció el bloqueo: la ejecución vieja ya no
    // puede cerrarlo, porque el cierre exige también el número de intento de la toma.
    [Test]
    public async Task A_stale_run_of_the_same_instance_cannot_close_the_newer_claim()
    {
        await using var host = OutboxHost.Create();
        var companyId = await host.CreateCompanyAsync();
        var id = await host.EnqueueAsync(companyId);
        await using var scope = host.OpenScope();
        var queue = scope.ServiceProvider.GetRequiredService<OutboxQueue>();
        var first = (await queue.ClaimAsync("worker-a", [host.MessageType], 10, CancellationToken.None)).Single();
        host.Clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        var second = (await queue.ClaimAsync("worker-a", [host.MessageType], 10, CancellationToken.None)).Single();

        var staleClosed = await queue.CompleteAsync("worker-a", first, CancellationToken.None);
        var currentClosed = await queue.CompleteAsync("worker-a", second, CancellationToken.None);

        var status = (await host.FindAsync(id)).Status;
        Assert.Multiple(() =>
        {
            Assert.That(staleClosed, Is.False);
            Assert.That(currentClosed, Is.True);
            Assert.That(status, Is.EqualTo(OutboxStatus.Done));
        });
    }
}
