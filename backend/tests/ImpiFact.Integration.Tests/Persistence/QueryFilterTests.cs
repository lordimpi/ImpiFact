using ImpiFact.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Integration.Tests.Persistence;

/// <summary>
/// Filtros globales de consulta: EF agrega solo el «WHERE company_id = empresa actual» y el «WHERE deleted_at
/// IS NULL» a toda consulta, para que ningún caso de uso vea datos de otra empresa ni filas borradas.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class QueryFilterTests
{
    [Test]
    public async Task A_company_never_sees_rows_of_another_company()
    {
        using var harness = new PersistenceHarness();
        var companyA = Guid.CreateVersion7();
        var companyB = Guid.CreateVersion7();
        var widgetA = Widgets.New(companyA, "tenant");
        var widgetB = Widgets.New(companyB, "tenant");
        await Widgets.SaveAsync(harness, widgetA);
        await Widgets.SaveAsync(harness, widgetB);

        await using var session = harness.Open(companyA);
        var visible = await session.Db.Widgets.Where(w => w.DisplayName == "tenant").Select(w => w.Id).ToListAsync();
        var foreign = await session.Db.Widgets.FirstOrDefaultAsync(w => w.Id == widgetB.Id);

        Assert.Multiple(() =>
        {
            Assert.That(visible, Is.EqualTo(new[] { widgetA.Id }));
            Assert.That(foreign, Is.Null);
        });
    }

    [Test]
    public async Task Without_a_company_no_tenant_rows_are_visible()
    {
        using var harness = new PersistenceHarness();
        var widget = Widgets.New(Guid.CreateVersion7(), "no company");
        await Widgets.SaveAsync(harness, widget);

        await using var session = harness.Open(companyId: null);
        var found = await session.Db.Widgets.FirstOrDefaultAsync(w => w.Id == widget.Id);

        Assert.That(found, Is.Null);
    }

    [Test]
    public async Task Soft_deleted_rows_are_hidden_unless_that_filter_is_ignored()
    {
        using var harness = new PersistenceHarness();
        var companyId = Guid.CreateVersion7();
        var widget = Widgets.New(companyId, "soft delete");
        widget.DeletedAt = harness.Clock.UtcNow;
        await Widgets.SaveAsync(harness, widget);

        await using var session = harness.Open(companyId);
        var filtered = await session.Db.Widgets.FirstOrDefaultAsync(w => w.Id == widget.Id);
        var unfiltered = await session.Db.Widgets.IgnoreQueryFilters([QueryFilters.SoftDelete])
            .FirstOrDefaultAsync(w => w.Id == widget.Id);

        Assert.Multiple(() =>
        {
            Assert.That(filtered, Is.Null);
            Assert.That(unfiltered?.Id, Is.EqualTo(widget.Id));
        });
    }

    [Test]
    public async Task Ignoring_only_the_soft_delete_filter_keeps_the_tenant_filter()
    {
        using var harness = new PersistenceHarness();
        var widget = Widgets.New(Guid.CreateVersion7(), "soft delete other company");
        widget.DeletedAt = harness.Clock.UtcNow;
        await Widgets.SaveAsync(harness, widget);

        await using var session = harness.Open(Guid.CreateVersion7());
        var found = await session.Db.Widgets.IgnoreQueryFilters([QueryFilters.SoftDelete])
            .FirstOrDefaultAsync(w => w.Id == widget.Id);

        Assert.That(found, Is.Null);
    }
}
