using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Integration.Tests.Persistence;

/// <summary>La unidad de trabajo agrupa los cambios en una transacción que se confirma o se revierte entera.</summary>
[TestFixture]
[Category("Integration")]
public sealed class UnitOfWorkTests
{
    [Test]
    public async Task Committing_keeps_the_saved_rows()
    {
        using var harness = new PersistenceHarness();
        var widget = Widgets.New(Guid.CreateVersion7(), "commit");

        await using (var session = harness.Open(widget.CompanyId))
        {
            await using var transaction = await session.UnitOfWork.BeginTransactionAsync(CancellationToken.None);
            session.Db.Widgets.Add(widget);
            await session.UnitOfWork.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }

        Assert.That(await CountAsync(harness, widget), Is.EqualTo(1));
    }

    [Test]
    public async Task Rolling_back_leaves_no_rows()
    {
        using var harness = new PersistenceHarness();
        var widget = Widgets.New(Guid.CreateVersion7(), "rollback");

        await using (var session = harness.Open(widget.CompanyId))
        {
            await using var transaction = await session.UnitOfWork.BeginTransactionAsync(CancellationToken.None);
            session.Db.Widgets.Add(widget);
            await session.UnitOfWork.SaveChangesAsync(CancellationToken.None);
            await transaction.RollbackAsync(CancellationToken.None);
        }

        Assert.That(await CountAsync(harness, widget), Is.Zero);
    }

    [Test]
    public async Task Disposing_without_commit_rolls_back()
    {
        using var harness = new PersistenceHarness();
        var widget = Widgets.New(Guid.CreateVersion7(), "dispose");

        await using (var session = harness.Open(widget.CompanyId))
        {
            await using var transaction = await session.UnitOfWork.BeginTransactionAsync(CancellationToken.None);
            session.Db.Widgets.Add(widget);
            await session.UnitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        Assert.That(await CountAsync(harness, widget), Is.Zero);
    }

    private static async Task<int> CountAsync(PersistenceHarness harness, Widget widget)
    {
        await using var session = harness.Open(widget.CompanyId);
        return await session.Db.Widgets.CountAsync(w => w.Id == widget.Id);
    }
}
