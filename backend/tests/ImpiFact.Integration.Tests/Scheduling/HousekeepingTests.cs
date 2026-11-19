using System.Text.Json.Nodes;
using ImpiFact.Domain.Identity;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Platform.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Integration.Tests.Scheduling;

/// <summary>
/// Limpieza diaria (<c>Housekeeping</c>; Arquitectura detallada, sección 8): borra los mensajes de la bandeja de salida
/// hechos hace más de 30 días, las llaves de idempotencia vencidas, los tokens de renovación expirados y los tokens para
/// restablecer la contraseña vencidos, por tandas.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class HousekeepingTests
{
    /// <summary>Cómo escribe EF Core el tope de filas de un DELETE por tandas: LIMIT en PostgreSQL, TOP en SQL Server.</summary>
    private static readonly string BatchLimit = TestDatabase.IsSqlServer ? "TOP(" : "LIMIT";

    [Test]
    public async Task Done_outbox_messages_older_than_30_days_are_deleted_and_the_rest_kept()
    {
        await using var host = SchedulingHost.Create();
        var now = host.Clock.UtcNow;
        var old = await AddMessageAsync(host, OutboxStatus.Done, processedAt: now.AddDays(-30).AddSeconds(-1));
        var boundary = await AddMessageAsync(host, OutboxStatus.Done, processedAt: now.AddDays(-30));
        var recent = await AddMessageAsync(host, OutboxStatus.Done, processedAt: now.AddDays(-1));
        var failed = await AddMessageAsync(host, OutboxStatus.Failed, processedAt: now.AddDays(-90));
        var pending = await AddMessageAsync(host, OutboxStatus.Pending, processedAt: null);

        var run = await host.RunJobAsync("Housekeeping");

        var left = await MessageIdsAsync(host, [old, boundary, recent, failed, pending]);
        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(ScheduledJobOutcome.Completed));
            Assert.That(left, Is.EquivalentTo(new[] { boundary, recent, failed, pending }));
        });
    }

    [Test]
    public async Task Expired_idempotency_keys_are_deleted_and_live_ones_kept()
    {
        await using var host = SchedulingHost.Create();
        var company = await host.CreateCompanyAsync();
        var now = host.Clock.UtcNow;
        var expired = await AddKeyAsync(host, company.Id, now.AddSeconds(-1));
        var live = await AddKeyAsync(host, company.Id, now.AddSeconds(1));

        await host.RunJobAsync("Housekeeping");

        await using var scope = host.Worker.OpenScope();
        var left = await SchedulingHost.Db(scope).Set<IdempotencyKey>().IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(key => key.CompanyId == company.Id).Select(key => key.Id).ToListAsync();
        Assert.That(left, Is.EqualTo(new[] { live }), $"expired {expired}");
    }

    [Test]
    public async Task Expired_refresh_tokens_are_deleted_with_their_rotation_chain_and_live_ones_kept()
    {
        await using var host = SchedulingHost.Create();
        var now = host.Clock.UtcNow;
        var userId = await AddUserAsync(host);

        // Cadena de rotación: cada token apunta al que lo reemplazó (replaced_by); el último sigue vivo.
        var live = await AddTokenAsync(host, userId, now.AddDays(3), replacedBy: null);
        var expiredRecently = await AddTokenAsync(host, userId, now.AddDays(-1), replacedBy: live);
        var expiredLongAgo = await AddTokenAsync(host, userId, now.AddDays(-5), replacedBy: expiredRecently);

        await host.RunJobAsync("Housekeeping");

        await using var scope = host.Worker.OpenScope();
        var left = await SchedulingHost.Db(scope).Set<RefreshToken>().Where(token => token.UserId == userId).Select(token => token.Id).ToListAsync();
        Assert.That(left, Is.EqualTo(new[] { live }), $"expired {expiredRecently}, {expiredLongAgo}");
    }

    [Test]
    public async Task Expired_password_reset_tokens_are_deleted_used_or_not_and_live_ones_kept()
    {
        await using var host = SchedulingHost.Create();
        var now = host.Clock.UtcNow;
        var userId = await AddUserAsync(host);
        var expired = await AddResetTokenAsync(host, userId, now.AddSeconds(-1));
        var used = await AddResetTokenAsync(host, userId, now.AddMinutes(-10), usedAt: now.AddMinutes(-35));
        var revoked = await AddResetTokenAsync(host, userId, now.AddDays(-2), revokedAt: now.AddDays(-2).AddMinutes(-20));
        var live = await AddResetTokenAsync(host, userId, now.AddMinutes(20));
        var liveButUsed = await AddResetTokenAsync(host, userId, now.AddMinutes(25), usedAt: now.AddMinutes(-5));

        var run = await host.RunJobAsync("Housekeeping");

        await using var scope = host.Worker.OpenScope();
        var left = await SchedulingHost.Db(scope).Set<PasswordResetToken>().Where(token => token.UserId == userId)
            .Select(token => token.Id).ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(ScheduledJobOutcome.Completed));
            Assert.That(left, Is.EquivalentTo(new[] { live, liveButUsed }), $"expired {expired}, {used}, {revoked}");
        });
    }

    [Test]
    public async Task Expired_password_reset_tokens_are_deleted_in_batches()
    {
        await using var host = SchedulingHost.Create(new Dictionary<string, string?> { ["ScheduledJobs:DeleteBatchSize"] = "2" });
        var now = host.Clock.UtcNow;
        var userId = await AddUserAsync(host);
        for (var index = 0; index < 5; index++)
        {
            await AddResetTokenAsync(host, userId, now.AddHours(-1 - index));
        }

        await host.RunJobAsync("Housekeeping");

        var deletes = host.Worker.Logs.Entries
            .Where(entry => entry.Contains("Executed DbCommand", StringComparison.Ordinal)
                && entry.Contains("DELETE FROM", StringComparison.Ordinal)
                && entry.Contains("password_reset_tokens", StringComparison.Ordinal))
            .ToList();
        await using var scope = host.Worker.OpenScope();
        Assert.Multiple(async () =>
        {
            Assert.That(deletes, Has.Count.GreaterThanOrEqualTo(3));
            Assert.That(deletes, Has.All.Contains(BatchLimit));
            Assert.That(await SchedulingHost.Db(scope).Set<PasswordResetToken>().CountAsync(token => token.UserId == userId), Is.Zero);
        });
    }

    [Test]
    public async Task A_backlog_larger_than_a_batch_is_deleted_in_several_batches()
    {
        await using var host = SchedulingHost.Create(new Dictionary<string, string?> { ["ScheduledJobs:DeleteBatchSize"] = "2" });
        var now = host.Clock.UtcNow;
        var old = new List<Guid>();
        for (var index = 0; index < 5; index++)
        {
            old.Add(await AddMessageAsync(host, OutboxStatus.Done, processedAt: now.AddDays(-40 - index)));
        }

        var userId = await AddUserAsync(host);
        var tokens = new List<Guid>();
        Guid? next = null;
        for (var index = 0; index < 5; index++)
        {
            next = await AddTokenAsync(host, userId, now.AddDays(-1 - index), replacedBy: next);
            tokens.Add(next.Value);
        }

        await host.RunJobAsync("Housekeeping");

        // Cada tanda es su propia sentencia DELETE con su límite de filas.
        var deletes = host.Worker.Logs.Entries
            .Where(entry => entry.Contains("Executed DbCommand", StringComparison.Ordinal) && entry.Contains("DELETE FROM", StringComparison.Ordinal))
            .ToList();
        await using var scope = host.Worker.OpenScope();
        Assert.Multiple(async () =>
        {
            Assert.That(deletes.Count(entry => entry.Contains("outbox_messages", StringComparison.Ordinal)), Is.GreaterThanOrEqualTo(3));
            Assert.That(deletes.Count(entry => entry.Contains("refresh_tokens", StringComparison.Ordinal)), Is.GreaterThanOrEqualTo(3));
            Assert.That(deletes, Has.All.Contains(BatchLimit));
            Assert.That(await MessageIdsAsync(host, old), Is.Empty);
            Assert.That(await SchedulingHost.Db(scope).Set<RefreshToken>().CountAsync(token => token.UserId == userId), Is.Zero);
        });
    }

    private static async Task<Guid> AddMessageAsync(SchedulingHost host, OutboxStatus status, DateTimeOffset? processedAt)
    {
        var message = new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            Type = "Test.Housekeeping",
            Payload = new JsonObject { ["reference"] = "R-1" },
            Status = status,
            NextAttemptAt = host.Clock.UtcNow.AddDays(-100),
            CreatedAt = host.Clock.UtcNow.AddDays(-100),
            ProcessedAt = processedAt,
        };
        await using var scope = host.Worker.OpenScope();
        var db = SchedulingHost.Db(scope);
        db.Add(message);
        await db.SaveChangesAsync();
        return message.Id;
    }

    private static async Task<List<Guid>> MessageIdsAsync(SchedulingHost host, IReadOnlyCollection<Guid> ids)
    {
        await using var scope = host.Worker.OpenScope();
        return await SchedulingHost.Db(scope).Set<OutboxMessage>().Where(message => ids.Contains(message.Id)).Select(message => message.Id).ToListAsync();
    }

    private static async Task<Guid> AddKeyAsync(SchedulingHost host, Guid companyId, DateTimeOffset expiresAt)
    {
        var key = new IdempotencyKey
        {
            Id = Guid.CreateVersion7(),
            CompanyId = companyId,
            Key = "key-" + Guid.NewGuid().ToString("N"),
            RequestHash = new string('a', 64),
            ResponseStatus = 200,
            ExpiresAt = expiresAt,
        };
        await using var scope = host.Worker.OpenScope();
        var db = SchedulingHost.Db(scope);
        db.Add(key);
        await db.SaveChangesAsync();
        return key.Id;
    }

    private static async Task<Guid> AddUserAsync(SchedulingHost host)
    {
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = $"user{Guid.NewGuid():N}@example.com",
            PasswordHash = "hash",
            Name = "Usuario",
            SecurityStamp = "stamp",
        };
        await using var scope = host.Worker.OpenScope();
        var db = SchedulingHost.Db(scope);
        db.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<Guid> AddTokenAsync(SchedulingHost host, Guid userId, DateTimeOffset expiresAt, Guid? replacedBy)
    {
        var token = new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TokenHash = Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray()),
            ExpiresAt = expiresAt,
            ReplacedBy = replacedBy,
        };
        await using var scope = host.Worker.OpenScope();
        var db = SchedulingHost.Db(scope);
        db.Add(token);
        await db.SaveChangesAsync();
        return token.Id;
    }

    private static async Task<Guid> AddResetTokenAsync(
        SchedulingHost host, Guid userId, DateTimeOffset expiresAt, DateTimeOffset? usedAt = null, DateTimeOffset? revokedAt = null)
    {
        var token = new PasswordResetToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TokenHash = Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray()),
            ExpiresAt = expiresAt,
            UsedAt = usedAt,
            RevokedAt = revokedAt,
        };
        await using var scope = host.Worker.OpenScope();
        var db = SchedulingHost.Db(scope);
        db.Add(token);
        await db.SaveChangesAsync();
        return token.Id;
    }
}
