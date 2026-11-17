using ImpiFact.Application.Abstractions.Billing;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Platform.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static ImpiFact.Integration.Tests.DocumentUtilities.UtilitiesHost;

namespace ImpiFact.Integration.Tests.Scheduling;

/// <summary>
/// El candado consultivo (advisory lock) de cada tarea diaria no queda tomado en el servidor después de soltarlo, de
/// cancelar la toma o de correr las tareas, aunque la conexión vuelva al pool.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class JobLockReleaseTests
{
    [Test]
    public async Task A_released_job_lock_leaves_no_advisory_lock_on_the_server()
    {
        await using var host = SchedulingHost.Create();
        var locks = host.Worker.Services.GetRequiredService<ScheduledJobLocks>();
        await using (var held = await locks.TryAcquireAsync("Housekeeping", CancellationToken.None))
        {
            Assert.That(held, Is.Not.Null);
            Assert.That(await AdvisoryLocksAsync(), Is.EqualTo(1));
        }

        Assert.That(await AdvisoryLocksAsync(), Is.EqualTo(0));

        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        Assert.CatchAsync<OperationCanceledException>(() => locks.TryAcquireAsync("Housekeeping", canceled.Token));
        Assert.That(await AdvisoryLocksAsync(), Is.EqualTo(0));

        var runs = await host.RunJobsAsync();
        Assert.That(await AdvisoryLocksAsync(), Is.EqualTo(0), string.Join(",", runs.Select(run => run.Outcome)));
    }

    private static async Task<long> AdvisoryLocksAsync()
    {
        await using var connection = new NpgsqlConnection(SchedulingEnvironment.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND granted", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
