using ImpiFact.Infrastructure.Platform.Scheduling;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Scheduling;

/// <summary>
/// El programador de las tareas diarias del Worker: corre cada tarea con un bloqueo de la base (advisory lock de
/// PostgreSQL), así dos instancias nunca corren la misma tarea a la vez, y la falla de una tarea no detiene a las demás.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class ScheduledJobsTests
{
    private static readonly string[] Jobs = ["CertificateExpiryWatcher", "Housekeeping", "ResolutionRangeWatcher"];

    [Test]
    public async Task A_daily_run_completes_the_three_jobs()
    {
        await using var host = SchedulingHost.Create();

        var runs = await host.RunJobsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(runs.Select(run => run.Job), Is.EquivalentTo(Jobs));
            Assert.That(runs.Select(run => run.Outcome), Is.All.EqualTo(ScheduledJobOutcome.Completed));
        });
    }

    [Test]
    public async Task A_job_that_another_instance_is_running_is_skipped_and_runs_once_it_is_free()
    {
        await using var running = SchedulingHost.Create();
        await using var other = SchedulingHost.Create();
        var locks = running.Worker.Services.GetRequiredService<ScheduledJobLocks>();

        IReadOnlyList<ScheduledJobRun> whileHeld;
        await using (var held = await locks.TryAcquireAsync("Housekeeping", CancellationToken.None))
        {
            Assert.That(held, Is.Not.Null);
            Assert.That(await locks.TryAcquireAsync("Housekeeping", CancellationToken.None), Is.Null, "a second holder");
            whileHeld = await other.RunJobsAsync();
        }

        var afterwards = await other.RunJobAsync("Housekeeping");

        Assert.Multiple(() =>
        {
            Assert.That(whileHeld.Single(run => run.Job == "Housekeeping").Outcome, Is.EqualTo(ScheduledJobOutcome.Skipped));
            Assert.That(whileHeld.Where(run => run.Job != "Housekeeping").Select(run => run.Outcome),
                Is.All.EqualTo(ScheduledJobOutcome.Completed));
            Assert.That(afterwards.Outcome, Is.EqualTo(ScheduledJobOutcome.Completed));
        });
    }

    [Test]
    public async Task Two_instances_running_the_jobs_at_once_notify_once()
    {
        await using var first = SchedulingHost.Create();
        await using var second = SchedulingHost.Create();
        var company = await first.CreateCompanyAsync();
        await first.AddCertificateAsync(company.Id, first.Clock.UtcNow.AddDays(14));
        await first.AddResolutionAsync(company.Id, 1, 100, new DateOnly(2026, 3, 10), nextNumber: 99);

        for (var round = 0; round < 3; round++)
        {
            await Task.WhenAll(first.RunJobsAsync(), second.RunJobsAsync(), first.RunJobsAsync(), second.RunJobsAsync());
        }

        Assert.Multiple(async () =>
        {
            Assert.That((await first.NotificationsAsync(company.Id)).Select(notification => notification.Type),
                Is.EquivalentTo(new[] { "CertificateExpiring", "ResolutionNumbersLow", "ResolutionExpiring" }));
            Assert.That(await first.NoticeEmailsAsync(company.Id), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void An_unknown_job_name_is_rejected()
    {
        Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await using var host = SchedulingHost.Create();
            await host.RunJobAsync("Unknown");
        });
    }

    [TestCase("2026-03-02T07:59:00Z", "08:00", 1)]
    [TestCase("2026-03-02T08:00:00Z", "08:00", 1440)]
    [TestCase("2026-03-02T13:00:00Z", "08:00", 1140)]
    [TestCase("2026-03-02T23:30:00Z", "00:15", 45)]
    public void The_next_run_is_the_next_daily_time_in_utc(string now, string runAt, int minutes)
    {
        var delay = ScheduledJobsService.DelayUntilNextRun(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture), TimeOnly.Parse(runAt, System.Globalization.CultureInfo.InvariantCulture));

        Assert.That(delay, Is.EqualTo(TimeSpan.FromMinutes(minutes)));
    }
}
