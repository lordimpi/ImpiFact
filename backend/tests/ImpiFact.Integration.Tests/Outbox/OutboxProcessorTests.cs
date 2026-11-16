using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Platform.Outbox;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Outbox;

/// <summary>
/// El procesador de la bandeja de salida (outbox; Arquitectura detallada, sección 8): toma los mensajes vencidos de
/// los tipos con handler, fija la empresa del mensaje como tenant, despacha al handler y reintenta con retroceso
/// (backoff: 10 s, 1 min, 5 min, 30 min, 2 h) hasta el quinto fallo, cuando el mensaje queda fallido y la empresa
/// recibe una notificación.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class OutboxProcessorTests
{
    [Test]
    public async Task A_handled_message_ends_done_with_its_processing_time()
    {
        await using var host = OutboxHost.Create();
        var companyId = await host.CreateCompanyAsync();
        var id = await host.EnqueueAsync(companyId);

        var claimed = await host.Processor.ProcessBatchAsync(CancellationToken.None);

        var message = await host.FindAsync(id);
        Assert.Multiple(() =>
        {
            Assert.That(claimed, Is.EqualTo(1));
            Assert.That(message.Status, Is.EqualTo(OutboxStatus.Done));
            Assert.That(message.ProcessedAt, Is.EqualTo(host.Clock.UtcNow));
            Assert.That(message.Attempts, Is.EqualTo(1));
            Assert.That(message.LockedBy, Is.Null);
            Assert.That(message.LockedUntil, Is.Null);
            Assert.That(message.LastError, Is.Null);
        });
    }

    [Test]
    public async Task The_handler_runs_as_the_system_for_the_company_of_the_message()
    {
        await using var host = OutboxHost.Create();
        var companyId = await host.CreateCompanyAsync();
        var companyMessage = await host.EnqueueAsync(companyId);
        var platformMessage = await host.EnqueueAsync(companyId: null);

        await host.Processor.ProcessBatchAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(host.Script.Seen[companyMessage], Is.EqualTo(((Guid?)companyId, ActorType.System, 1)));
            Assert.That(host.Script.Seen[platformMessage], Is.EqualTo(((Guid?)null, ActorType.System, 1)));
        });
    }

    [Test]
    public async Task A_message_without_a_registered_handler_stays_pending_and_untouched()
    {
        await using var host = OutboxHost.Create();
        var companyId = await host.CreateCompanyAsync();
        var unhandled = await host.EnqueueAsync(companyId, host.UnhandledType);
        var handled = await host.EnqueueAsync(companyId);

        var claimed = await host.Processor.ProcessBatchAsync(CancellationToken.None);

        var message = await host.FindAsync(unhandled);
        Assert.Multiple(async () =>
        {
            Assert.That(claimed, Is.EqualTo(1));
            Assert.That((await host.FindAsync(handled)).Status, Is.EqualTo(OutboxStatus.Done));
            Assert.That(message.Status, Is.EqualTo(OutboxStatus.Pending));
            Assert.That(message.Attempts, Is.Zero);
            Assert.That(message.LockedBy, Is.Null);
            Assert.That(message.LockedUntil, Is.Null);
            Assert.That(message.NextAttemptAt, Is.EqualTo(host.Clock.UtcNow));
        });
    }

    [Test]
    public async Task A_message_is_not_taken_before_its_next_attempt_time()
    {
        await using var host = OutboxHost.Create();
        var companyId = await host.CreateCompanyAsync();
        var id = await host.EnqueueAsync(companyId, notBefore: host.Clock.UtcNow.AddMinutes(1));

        var early = await host.Processor.ProcessBatchAsync(CancellationToken.None);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var due = await host.Processor.ProcessBatchAsync(CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(early, Is.Zero);
            Assert.That(due, Is.EqualTo(1));
            Assert.That((await host.FindAsync(id)).Status, Is.EqualTo(OutboxStatus.Done));
        });
    }

    [Test]
    public async Task A_batch_takes_the_oldest_due_messages_first_up_to_the_batch_size()
    {
        await using var host = OutboxHost.Create(new Dictionary<string, string?> { ["Outbox:BatchSize"] = "2" });
        var companyId = await host.CreateCompanyAsync();
        var newest = await host.EnqueueAsync(companyId, notBefore: host.Clock.UtcNow.AddSeconds(-1));
        var oldest = await host.EnqueueAsync(companyId, notBefore: host.Clock.UtcNow.AddSeconds(-3));
        var middle = await host.EnqueueAsync(companyId, notBefore: host.Clock.UtcNow.AddSeconds(-2));

        var claimed = await host.Processor.ProcessBatchAsync(CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(claimed, Is.EqualTo(2));
            Assert.That((await host.FindAsync(oldest)).Status, Is.EqualTo(OutboxStatus.Done));
            Assert.That((await host.FindAsync(middle)).Status, Is.EqualTo(OutboxStatus.Done));
            Assert.That((await host.FindAsync(newest)).Status, Is.EqualTo(OutboxStatus.Pending));
        });
    }

    [Test]
    public async Task Failures_are_retried_after_10_s_1_min_5_min_and_30_min_and_the_fifth_fails_with_one_notification()
    {
        await using var host = OutboxHost.Create();
        var companyId = await host.CreateCompanyAsync();
        var id = await host.EnqueueAsync(companyId);
        host.Script.FailWith(id, () => new InvalidOperationException("The DIAN did not answer."));
        TimeSpan[] backoff = [TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30)];

        for (var attempt = 1; attempt <= backoff.Length; attempt++)
        {
            Assert.That(await host.Processor.ProcessBatchAsync(CancellationToken.None), Is.EqualTo(1), $"attempt {attempt}");
            var retried = await host.FindAsync(id);
            Assert.Multiple(() =>
            {
                Assert.That(retried.Status, Is.EqualTo(OutboxStatus.Pending), $"attempt {attempt}");
                Assert.That(retried.Attempts, Is.EqualTo(attempt));
                Assert.That(retried.NextAttemptAt, Is.EqualTo(host.Clock.UtcNow + backoff[attempt - 1]), $"attempt {attempt}");
                Assert.That(retried.LastError, Does.Contain("The DIAN did not answer."));
                Assert.That(retried.LockedBy, Is.Null);
                Assert.That(retried.LockedUntil, Is.Null);
            });

            // Un segundo antes del plazo todavía no se toma; en el plazo exacto sí.
            host.Clock.Advance(backoff[attempt - 1] - TimeSpan.FromSeconds(1));
            Assert.That(await host.Processor.ProcessBatchAsync(CancellationToken.None), Is.Zero, $"before attempt {attempt + 1}");
            host.Clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.That(await host.Processor.ProcessBatchAsync(CancellationToken.None), Is.EqualTo(1));

        var failed = await host.FindAsync(id);
        var notifications = await host.NotificationsAsync(companyId);
        Assert.Multiple(() =>
        {
            Assert.That(failed.Status, Is.EqualTo(OutboxStatus.Failed));
            Assert.That(failed.Attempts, Is.EqualTo(5));
            Assert.That(failed.ProcessedAt, Is.EqualTo(host.Clock.UtcNow));
            Assert.That(failed.LockedBy, Is.Null);
            Assert.That(host.Script.Seen[id].Attempt, Is.EqualTo(5));
            Assert.That(notifications, Has.Count.EqualTo(1));
            Assert.That(notifications[0].Type, Is.EqualTo(OutboxNotifications.FailedType));
            Assert.That(notifications[0].UserId, Is.Null);
            Assert.That(notifications[0].Body, Does.Contain(host.MessageType).And.Contain(id.ToString()));
            Assert.That(notifications[0].Body, Does.Not.Contain("R-1"), "The notification never copies the payload.");
        });

        // Ya fallido, no se vuelve a tomar.
        host.Clock.Advance(TimeSpan.FromHours(3));
        Assert.That(await host.Processor.ProcessBatchAsync(CancellationToken.None), Is.Zero);
    }

    [Test]
    public async Task A_permanent_failure_fails_the_message_on_its_first_attempt()
    {
        await using var host = OutboxHost.Create();
        var companyId = await host.CreateCompanyAsync();
        var id = await host.EnqueueAsync(companyId);
        host.Script.FailWith(id, () => new PermanentOutboxFailureException("The document no longer exists."));

        await host.Processor.ProcessBatchAsync(CancellationToken.None);

        var message = await host.FindAsync(id);
        Assert.Multiple(async () =>
        {
            Assert.That(message.Status, Is.EqualTo(OutboxStatus.Failed));
            Assert.That(message.Attempts, Is.EqualTo(1));
            Assert.That(message.ProcessedAt, Is.EqualTo(host.Clock.UtcNow));
            Assert.That(message.LastError, Does.Contain("The document no longer exists."));
            Assert.That(await host.NotificationsAsync(companyId), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task A_failed_platform_message_creates_no_notification()
    {
        await using var host = OutboxHost.Create();
        var id = await host.EnqueueAsync(companyId: null);
        host.Script.FailWith(id, () => new PermanentOutboxFailureException("Not retryable."));

        await host.Processor.ProcessBatchAsync(CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That((await host.FindAsync(id)).Status, Is.EqualTo(OutboxStatus.Failed));
            Assert.That(await host.CountNotificationsAboutAsync(id), Is.Zero);
        });
    }

    [Test]
    public async Task The_last_error_is_cut_to_2000_characters()
    {
        await using var host = OutboxHost.Create();
        var companyId = await host.CreateCompanyAsync();
        var id = await host.EnqueueAsync(companyId);
        host.Script.FailWith(id, () => new InvalidOperationException(new string('x', 5000)));

        await host.Processor.ProcessBatchAsync(CancellationToken.None);

        Assert.That((await host.FindAsync(id)).LastError, Has.Length.EqualTo(2000));
    }

    [Test]
    public async Task One_failing_message_does_not_stop_the_others_in_the_same_batch()
    {
        await using var host = OutboxHost.Create();
        var failingCompany = await host.CreateCompanyAsync();
        var otherCompany = await host.CreateCompanyAsync();
        var failing = await host.EnqueueAsync(failingCompany, notBefore: host.Clock.UtcNow.AddSeconds(-1));
        var other = await host.EnqueueAsync(otherCompany);
        host.Script.FailWith(failing, () => new InvalidOperationException("Boom."));

        var claimed = await host.Processor.ProcessBatchAsync(CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(claimed, Is.EqualTo(2));
            Assert.That((await host.FindAsync(failing)).Status, Is.EqualTo(OutboxStatus.Pending));
            Assert.That((await host.FindAsync(other)).Status, Is.EqualTo(OutboxStatus.Done));
        });
    }

    [Test]
    public async Task A_message_whose_lock_expired_is_taken_again()
    {
        await using var host = OutboxHost.Create();
        var companyId = await host.CreateCompanyAsync();
        var id = await host.EnqueueAsync(companyId);
        await using (var scope = host.OpenScope())
        {
            // Una instancia que se cayó después de tomarlo: el mensaje queda en proceso con su bloqueo.
            await scope.ServiceProvider.GetRequiredService<OutboxQueue>()
                .ClaimAsync("crashed-worker", [host.MessageType], batchSize: 10, CancellationToken.None);
        }

        host.Clock.Advance(TimeSpan.FromMinutes(5));
        var whileLocked = await host.Processor.ProcessBatchAsync(CancellationToken.None);
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        var afterExpiry = await host.Processor.ProcessBatchAsync(CancellationToken.None);

        var message = await host.FindAsync(id);
        Assert.Multiple(() =>
        {
            Assert.That(whileLocked, Is.Zero);
            Assert.That(afterExpiry, Is.EqualTo(1));
            Assert.That(message.Status, Is.EqualTo(OutboxStatus.Done));
            Assert.That(message.Attempts, Is.EqualTo(2));
            Assert.That(host.Script.Seen[id].Attempt, Is.EqualTo(2));
        });
    }

    // Un mensaje que tumba el proceso nunca llega a registrar su falla: cada toma suma un intento. Al pasar del
    // máximo queda fallido sin volver a ejecutar el handler.
    [Test]
    public async Task A_message_reclaimed_beyond_the_maximum_attempts_fails_without_running_the_handler()
    {
        await using var host = OutboxHost.Create();
        var companyId = await host.CreateCompanyAsync();
        var id = await host.EnqueueAsync(companyId);
        for (var crash = 0; crash < 5; crash++)
        {
            await using var scope = host.OpenScope();
            await scope.ServiceProvider.GetRequiredService<OutboxQueue>()
                .ClaimAsync("crashing-worker", [host.MessageType], batchSize: 10, CancellationToken.None);
            host.Clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        }

        await host.Processor.ProcessBatchAsync(CancellationToken.None);

        var message = await host.FindAsync(id);
        var notifications = await host.CountNotificationsAboutAsync(id);
        Assert.Multiple(() =>
        {
            Assert.That(message.Status, Is.EqualTo(OutboxStatus.Failed));
            Assert.That(message.Attempts, Is.EqualTo(6));
            Assert.That(host.Script.Seen.ContainsKey(id), Is.False, "The handler must not run again.");
            Assert.That(notifications, Is.EqualTo(1));
        });
    }

    // Un handler que tarda más que su plazo se cancela y el mensaje se reintenta, antes de que venza el bloqueo y
    // otra instancia lo tome mientras este sigue corriendo.
    [Test]
    public async Task A_handler_that_exceeds_its_timeout_is_cancelled_and_retried()
    {
        await using var host = OutboxHost.Create(new Dictionary<string, string?> { ["Outbox:HandlerTimeoutSeconds"] = "1" });
        var companyId = await host.CreateCompanyAsync();
        var id = await host.EnqueueAsync(companyId);
        host.Script.Hanging[id] = true;

        await host.Processor.ProcessBatchAsync(CancellationToken.None);

        var message = await host.FindAsync(id);
        Assert.Multiple(() =>
        {
            Assert.That(message.Status, Is.EqualTo(OutboxStatus.Pending));
            Assert.That(message.LastError, Does.StartWith(nameof(TimeoutException)));
            Assert.That(message.NextAttemptAt, Is.EqualTo(host.Clock.UtcNow.AddSeconds(10)));
        });
    }

    // Si el Worker se apaga justo después de que el handler terminó bien, el resultado igual se guarda: si no, el
    // mensaje se volvería a ejecutar al vencer su bloqueo.
    [Test]
    public async Task The_outcome_is_recorded_even_when_shutdown_starts_after_the_handler_succeeded()
    {
        await using var host = OutboxHost.Create();
        var companyId = await host.CreateCompanyAsync();
        var id = await host.EnqueueAsync(companyId);
        using var shutdown = new CancellationTokenSource();
        host.Script.AfterSuccess = shutdown.Cancel;

        await host.Processor.ProcessBatchAsync(shutdown.Token);

        Assert.That((await host.FindAsync(id)).Status, Is.EqualTo(OutboxStatus.Done));
    }

    [Test]
    public async Task The_handler_hears_of_the_final_failure_before_it_is_recorded_so_a_crash_leaves_the_message_to_be_taken_again()
    {
        await using var host = OutboxHost.Create();
        var companyId = await host.CreateCompanyAsync();
        var id = await host.EnqueueAsync(companyId);
        host.Script.FailWith(id, () => new PermanentOutboxFailureException("The document does not exist."));
        OutboxStatus? seenByTheHandler = null;
        var notificationsSeen = -1;
        host.Script.OnFailed = async messageId =>
        {
            seenByTheHandler = (await host.FindAsync(messageId)).Status;
            notificationsSeen = await host.CountNotificationsAboutAsync(messageId);
        };

        await host.Processor.ProcessBatchAsync(CancellationToken.None);

        Assert.Multiple(async () =>
        {
            // Si el proceso se cae aquí, el mensaje sigue en proceso y se vuelve a tomar al vencer su bloqueo.
            Assert.That(seenByTheHandler, Is.EqualTo(OutboxStatus.Processing));
            Assert.That(notificationsSeen, Is.Zero);
            Assert.That((await host.FindAsync(id)).Status, Is.EqualTo(OutboxStatus.Failed));
            Assert.That(await host.CountNotificationsAboutAsync(id), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task A_message_reclaimed_beyond_the_maximum_attempts_lets_its_handler_record_the_failure()
    {
        await using var host = OutboxHost.Create(new Dictionary<string, string?> { ["Outbox:MaxAttempts"] = "1" });
        var companyId = await host.CreateCompanyAsync();
        var id = await host.EnqueueAsync(companyId);
        var failedCalls = 0;
        host.Script.OnFailed = _ =>
        {
            Interlocked.Increment(ref failedCalls);
            return Task.CompletedTask;
        };
        await using (var scope = host.OpenScope())
        {
            // Una toma que se cayó sin registrar su resultado (por ejemplo, durante el aviso al handler).
            await scope.ServiceProvider.GetRequiredService<OutboxQueue>()
                .ClaimAsync("crashing-worker", [host.MessageType], batchSize: 10, CancellationToken.None);
        }

        host.Clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        await host.Processor.ProcessBatchAsync(CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(failedCalls, Is.EqualTo(1));
            Assert.That((await host.FindAsync(id)).Status, Is.EqualTo(OutboxStatus.Failed));
        });
    }

    [Test]
    public async Task The_failure_notification_is_written_in_spanish()
    {
        await using var host = OutboxHost.Create();
        var companyId = await host.CreateCompanyAsync();
        var id = await host.EnqueueAsync(companyId);
        host.Script.FailWith(id, () => new PermanentOutboxFailureException("Rejected."));

        await host.Processor.ProcessBatchAsync(CancellationToken.None);

        var notification = (await host.NotificationsAsync(companyId)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(notification.Title, Is.EqualTo("Una tarea en segundo plano falló"));
            Assert.That(notification.Body, Is.EqualTo(
                $"La tarea «{host.MessageType}» (mensaje {id}) falló después de 1 intento(s) y no se reintentará automáticamente."));
        });
    }
}
