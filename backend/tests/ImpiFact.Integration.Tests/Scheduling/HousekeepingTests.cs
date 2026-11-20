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
    /// <summary>Cómo escribe EF Core el tope de filas de un DELETE por tandas: LIMIT en PostgreSQL y MySQL, TOP en SQL Server.</summary>
    private static readonly string BatchLimit = TestDatabase.IsSqlServer ? "TOP(" : "LIMIT";

    /// <summary>
    /// Las sentencias DELETE que ejecutó el Worker, con los espacios y saltos de línea reducidos a uno: el proveedor de MySQL
    /// escribe <c>DELETE</c> y <c>FROM</c> en líneas distintas.
    /// </summary>
    private static List<string> Deletes(SchedulingHost host) =>
    [
        .. host.Worker.Logs.Entries
            .Where(entry => entry.Contains("Executed DbCommand", StringComparison.Ordinal))
            .Select(entry => System.Text.RegularExpressions.Regex.Replace(entry, @"\s+", " "))
            .Where(entry => entry.Contains("DELETE FROM", StringComparison.Ordinal)),
    ];

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

        var deletes = Deletes(host).Where(entry => entry.Contains("password_reset_tokens", StringComparison.Ordinal)).ToList();
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
        var deletes = Deletes(host);
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

    // Si se acortó Jwt:RefreshDays entre dos rotaciones, el sucesor vence antes que su antecesor: borrar por fecha de
    // vencimiento quitaría primero la fila apuntada. Cada caso usa su propio host y su propio usuario: una cadena que no
    // se pudiera borrar haría fallar todas las corridas siguientes.
    [Test]
    public async Task A_successor_that_expires_before_its_predecessor_is_deleted_with_it()
    {
        await using var host = SchedulingHost.Create();
        var now = host.Clock.UtcNow;
        var userId = await AddUserAsync(host);
        var successor = await AddTokenAsync(host, userId, now.AddDays(-20), replacedBy: null);
        await AddTokenAsync(host, userId, now.AddDays(-1), replacedBy: successor);

        var run = await host.RunJobAsync("Housekeeping");

        await AssertTokensLeftAsync(host, run, userId, []);
    }

    // Tokens que vencen en el mismo instante no tienen un orden: la cadena se borra entera igual, en una tanda o en
    // varias, armada del más antiguo al más nuevo o al revés.
    [TestCase(2, true)]
    [TestCase(2, false)]
    [TestCase(100, true)]
    [TestCase(100, false)]
    public async Task A_chain_whose_tokens_expire_at_the_same_instant_is_deleted_whole(int batchSize, bool builtOldestFirst)
    {
        await using var host = SchedulingHost.Create(new Dictionary<string, string?>
        {
            ["ScheduledJobs:DeleteBatchSize"] = batchSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
        var expiresAt = host.Clock.UtcNow.AddDays(-2);
        var userId = await AddUserAsync(host);
        Guid? previous = null;
        for (var index = 0; index < 7; index++)
        {
            if (builtOldestFirst)
            {
                // Cada token nuevo reemplaza al anterior: el anterior pasa a apuntar a él.
                var id = await AddTokenAsync(host, userId, expiresAt, replacedBy: null);
                if (previous is { } replaced)
                {
                    await SetReplacedByAsync(host, replaced, id);
                }

                previous = id;
            }
            else
            {
                previous = await AddTokenAsync(host, userId, expiresAt, replacedBy: previous);
            }
        }

        var run = await host.RunJobAsync("Housekeeping");

        await AssertTokensLeftAsync(host, run, userId, []);
    }

    // Un token vencido al que apunta uno vivo se queda; lo que ese token vencido apunta (su sucesor, también vencido) se
    // borra, y la referencia del que se queda se suelta.
    [Test]
    public async Task A_token_that_a_live_token_points_to_is_kept_and_its_expired_successor_deleted()
    {
        await using var host = SchedulingHost.Create();
        var now = host.Clock.UtcNow;
        var userId = await AddUserAsync(host);
        var expiredSuccessor = await AddTokenAsync(host, userId, now.AddDays(-3), replacedBy: null);
        var keptByLive = await AddTokenAsync(host, userId, now.AddDays(-2), replacedBy: expiredSuccessor);
        var live = await AddTokenAsync(host, userId, now.AddDays(5), replacedBy: keptByLive);
        var stray = await AddTokenAsync(host, userId, now.AddDays(-9), replacedBy: null);

        var run = await host.RunJobAsync("Housekeeping");

        await AssertTokensLeftAsync(host, run, userId, [live, keptByLive]);
        await using var scope = host.Worker.OpenScope();
        var tokens = await SchedulingHost.Db(scope).Set<RefreshToken>().AsNoTracking().Where(token => token.UserId == userId).ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(tokens.Single(token => token.Id == live).ReplacedBy, Is.EqualTo(keptByLive), "The live token keeps its reference.");
            Assert.That(tokens.Single(token => token.Id == keptByLive).ReplacedBy, Is.Null, $"stray {stray}, successor {expiredSuccessor}");
        });
    }

    private static async Task AssertTokensLeftAsync(SchedulingHost host, ScheduledJobRun run, Guid userId, Guid[] expected)
    {
        await using var scope = host.Worker.OpenScope();
        var left = await SchedulingHost.Db(scope).Set<RefreshToken>().Where(token => token.UserId == userId).Select(token => token.Id).ToListAsync();
        var errors = string.Join("\n", host.Worker.Logs.Entries.Where(entry => entry.StartsWith("Error", StringComparison.Ordinal)).Take(3));
        Assert.Multiple(() =>
        {
            Assert.That(run.Outcome, Is.EqualTo(ScheduledJobOutcome.Completed), errors);
            Assert.That(left, Is.EquivalentTo(expected));
        });
    }

    private static async Task SetReplacedByAsync(SchedulingHost host, Guid id, Guid replacedBy)
    {
        await using var scope = host.Worker.OpenScope();
        await SchedulingHost.Db(scope).Set<RefreshToken>().Where(token => token.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(token => token.ReplacedBy, replacedBy));
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
