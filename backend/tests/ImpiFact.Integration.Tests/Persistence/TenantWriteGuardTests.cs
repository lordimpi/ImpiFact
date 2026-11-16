using ImpiFact.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Integration.Tests.Persistence;

/// <summary>
/// El filtro global solo protege las lecturas. Estas pruebas cubren las escrituras: una solicitud de la
/// empresa A no puede crear, cambiar ni borrar filas de la empresa B, ni mover una fila a otra empresa.
/// Solo la plataforma y los procesos internos escriben para cualquier empresa.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class TenantWriteGuardTests
{
    [Test]
    public async Task A_company_cannot_insert_a_row_for_another_company()
    {
        using var harness = new PersistenceHarness();
        var companyA = Guid.CreateVersion7();
        await using var session = harness.Open(companyA);

        session.Db.Widgets.Add(Widgets.New(Guid.CreateVersion7(), "foreign"));

        Assert.ThrowsAsync<InvalidOperationException>(() => session.Db.SaveChangesAsync());
    }

    [Test]
    public async Task A_company_cannot_overwrite_another_company_row_by_attaching_it()
    {
        using var harness = new PersistenceHarness();
        var companyB = Guid.CreateVersion7();
        var original = Widgets.New(companyB, "original");
        await Widgets.SaveAsync(harness, original);

        await using (var session = harness.Open(Guid.CreateVersion7()))
        {
            var forged = Widgets.New(companyB, "hijacked");
            forged.Id = original.Id;
            session.Db.Entry(forged).Property(nameof(Widget.RowVersion)).CurrentValue = 1L;
            session.Db.Widgets.Update(forged);

            Assert.ThrowsAsync<InvalidOperationException>(() => session.Db.SaveChangesAsync());
        }

        Assert.That(await NameOfAsync(harness, original), Is.EqualTo("original"));
    }

    [Test]
    public async Task A_row_claimed_with_the_own_company_id_still_cannot_overwrite_another_company_row()
    {
        using var harness = new PersistenceHarness();
        var original = Widgets.New(Guid.CreateVersion7(), "original");
        await Widgets.SaveAsync(harness, original);
        var companyA = Guid.CreateVersion7();

        await using (var session = harness.Open(companyA))
        {
            var forged = Widgets.New(companyA, "hijacked");
            forged.Id = original.Id;
            session.Db.Entry(forged).Property(nameof(Widget.RowVersion)).CurrentValue = 1L;
            session.Db.Widgets.Update(forged);

            Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => session.Db.SaveChangesAsync());
        }

        Assert.That(await NameOfAsync(harness, original), Is.EqualTo("original"));
    }

    [Test]
    public async Task A_company_cannot_delete_another_company_row()
    {
        using var harness = new PersistenceHarness();
        var original = Widgets.New(Guid.CreateVersion7(), "original");
        await Widgets.SaveAsync(harness, original);

        await using (var session = harness.Open(Guid.CreateVersion7()))
        {
            var forged = Widgets.New(original.CompanyId, "original");
            forged.Id = original.Id;
            session.Db.Widgets.Remove(forged);

            Assert.ThrowsAsync<InvalidOperationException>(() => session.Db.SaveChangesAsync());
        }

        Assert.That(await NameOfAsync(harness, original), Is.EqualTo("original"));
    }

    [Test]
    public async Task A_row_cannot_be_moved_to_another_company()
    {
        using var harness = new PersistenceHarness();
        var widget = Widgets.New(Guid.CreateVersion7(), "mine");
        await Widgets.SaveAsync(harness, widget);

        await using var session = harness.Open(widget.CompanyId);
        var tracked = await session.Db.Widgets.SingleAsync(w => w.Id == widget.Id);
        tracked.CompanyId = Guid.CreateVersion7();

        Assert.ThrowsAsync<InvalidOperationException>(() => session.Db.SaveChangesAsync());
    }

    [Test]
    public async Task A_company_still_writes_its_own_rows()
    {
        using var harness = new PersistenceHarness();
        var widget = Widgets.New(Guid.CreateVersion7(), "mine");
        await Widgets.SaveAsync(harness, widget);

        await using (var session = harness.Open(widget.CompanyId))
        {
            var tracked = await session.Db.Widgets.SingleAsync(w => w.Id == widget.Id);
            tracked.DisplayName = "renamed";
            await session.Db.SaveChangesAsync();
        }

        Assert.That(await NameOfAsync(harness, widget), Is.EqualTo("renamed"));
    }

    [TestCase(ActorType.Platform)]
    [TestCase(ActorType.System)]
    public async Task Platform_and_system_actors_write_for_any_company(ActorType actor)
    {
        using var harness = new PersistenceHarness();
        var widget = Widgets.New(Guid.CreateVersion7(), "created by " + actor);

        await using (var session = harness.Open(companyId: null, actor))
        {
            session.Db.Widgets.Add(widget);
            await session.Db.SaveChangesAsync();
        }

        Assert.That(await NameOfAsync(harness, widget), Is.EqualTo("created by " + actor));
    }

    [Test]
    public async Task A_rolled_back_command_leaves_nothing_for_a_later_save()
    {
        using var harness = new PersistenceHarness();
        var widget = Widgets.New(Guid.CreateVersion7(), "discarded");

        await using (var session = harness.Open(widget.CompanyId))
        {
            await using (var transaction = await session.UnitOfWork.BeginTransactionAsync(CancellationToken.None))
            {
                session.Db.Widgets.Add(widget);
                await transaction.RollbackAsync(CancellationToken.None);
            }

            await session.UnitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        Assert.That(await NameOfAsync(harness, widget), Is.Null);
    }

    private static async Task<string?> NameOfAsync(PersistenceHarness harness, Widget widget)
    {
        await using var session = harness.Open(widget.CompanyId);
        return await session.Db.Widgets.Where(w => w.Id == widget.Id).Select(w => w.DisplayName).SingleOrDefaultAsync();
    }
}
