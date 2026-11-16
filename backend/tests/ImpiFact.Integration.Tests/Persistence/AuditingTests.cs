using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Integration.Tests.Persistence;

/// <summary>
/// El interceptor de guardado (código que EF ejecuta justo antes de escribir) llena created_at y updated_at
/// con el reloj de la aplicación y sube row_version, la versión de fila que detecta escrituras simultáneas.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class AuditingTests
{
    [Test]
    public async Task Inserting_sets_both_timestamps_and_the_first_row_version()
    {
        using var harness = new PersistenceHarness();
        var companyId = Guid.CreateVersion7();
        var widget = Widgets.New(companyId, "insert");

        await using (var session = harness.Open(companyId))
        {
            session.Db.Widgets.Add(widget);
            await session.Db.SaveChangesAsync();
        }

        await using var reader = harness.Open(companyId);
        var stored = await reader.Db.Widgets.SingleAsync(w => w.Id == widget.Id);
        Assert.Multiple(() =>
        {
            Assert.That(stored.CreatedAt, Is.EqualTo(harness.Clock.UtcNow));
            Assert.That(stored.UpdatedAt, Is.EqualTo(harness.Clock.UtcNow));
            Assert.That(stored.RowVersion, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Updating_moves_updated_at_keeps_created_at_and_increments_the_row_version()
    {
        using var harness = new PersistenceHarness();
        var companyId = Guid.CreateVersion7();
        var widget = Widgets.New(companyId, "update");
        var createdAt = harness.Clock.UtcNow;
        await Widgets.SaveAsync(harness, widget);

        harness.Clock.Advance(TimeSpan.FromMinutes(5));
        await using (var session = harness.Open(companyId))
        {
            var tracked = await session.Db.Widgets.SingleAsync(w => w.Id == widget.Id);
            tracked.DisplayName = "update renamed";
            await session.Db.SaveChangesAsync();
        }

        await using var reader = harness.Open(companyId);
        var stored = await reader.Db.Widgets.SingleAsync(w => w.Id == widget.Id);
        Assert.Multiple(() =>
        {
            Assert.That(stored.CreatedAt, Is.EqualTo(createdAt));
            Assert.That(stored.UpdatedAt, Is.EqualTo(harness.Clock.UtcNow));
            Assert.That(stored.RowVersion, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Saving_a_stale_copy_throws_a_concurrency_exception()
    {
        using var harness = new PersistenceHarness();
        var companyId = Guid.CreateVersion7();
        var widget = Widgets.New(companyId, "stale");
        await Widgets.SaveAsync(harness, widget);

        await using var first = harness.Open(companyId);
        await using var second = harness.Open(companyId);
        var firstCopy = await first.Db.Widgets.SingleAsync(w => w.Id == widget.Id);
        var secondCopy = await second.Db.Widgets.SingleAsync(w => w.Id == widget.Id);

        firstCopy.DisplayName = "stale first";
        await first.Db.SaveChangesAsync();
        secondCopy.DisplayName = "stale second";

        Assert.That(async () => await second.Db.SaveChangesAsync(), Throws.InstanceOf<DbUpdateConcurrencyException>());
    }
}
