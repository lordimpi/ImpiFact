using System.Text.Json.Nodes;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Integration.Tests.Persistence;

/// <summary>
/// Conversiones de valores: instantes en UTC (tiempo universal coordinado, sin desfase horario), JSON con
/// opciones fijas y llaves UUID v7 (identificadores únicos que empiezan por la hora, así se ordenan al crearse).
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class ValueConversionTests
{
    [Test]
    public async Task Instants_with_an_offset_come_back_as_the_same_instant_in_utc()
    {
        using var harness = new PersistenceHarness();
        var widget = Widgets.New(Guid.CreateVersion7(), "utc");
        var colombia = new DateTimeOffset(2026, 3, 1, 8, 30, 0, TimeSpan.FromHours(-5));
        widget.OccurredAt = colombia;
        await Widgets.SaveAsync(harness, widget);

        await using var session = harness.Open(widget.CompanyId);
        var stored = await session.Db.Widgets.SingleAsync(w => w.Id == widget.Id);

        Assert.Multiple(() =>
        {
            Assert.That(stored.OccurredAt.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(stored.OccurredAt.UtcDateTime, Is.EqualTo(colombia.UtcDateTime));
        });
    }

    [Test]
    public async Task Json_values_round_trip_and_are_stored_with_snake_case_names()
    {
        using var harness = new PersistenceHarness();
        var widget = Widgets.New(Guid.CreateVersion7(), "json");
        widget.Details = new WidgetDetails { Tags = ["a", "b"], Note = "café" };
        await Widgets.SaveAsync(harness, widget);

        await using var session = harness.Open(widget.CompanyId);
        var stored = await session.Db.Widgets.SingleAsync(w => w.Id == widget.Id);
        await using var connection = await session.Reads.OpenAsync(CancellationToken.None);
        var raw = await connection.QuerySingleAsync<string>(
            "select details from widgets where company_id = @CompanyId and id = @Id",
            new { widget.CompanyId, widget.Id });

        Assert.Multiple(() =>
        {
            Assert.That(stored.Details.Tags, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(stored.Details.Note, Is.EqualTo("café"));
            // PostgreSQL guarda jsonb con su propio formato (claves ordenadas, espacios); SQL Server guarda el texto tal como lo
            // escribe el serializador. En los dos, las claves van en snake_case.
            if (TestDatabase.IsSqlServer)
            {
                Assert.That(JsonNode.DeepEquals(JsonNode.Parse(raw), JsonNode.Parse("""{"note": "café", "tags": ["a", "b"]}""")), Is.True, raw);
            }
            else
            {
                Assert.That(raw, Is.EqualTo("""{"note": "café", "tags": ["a", "b"]}"""));
            }
        });
    }

    [Test]
    public async Task Json_changes_made_inside_the_same_object_are_detected_and_saved()
    {
        using var harness = new PersistenceHarness();
        var widget = Widgets.New(Guid.CreateVersion7(), "json change");
        await Widgets.SaveAsync(harness, widget);

        await using (var session = harness.Open(widget.CompanyId))
        {
            var tracked = await session.Db.Widgets.SingleAsync(w => w.Id == widget.Id);
            tracked.Details.Note = "changed";
            tracked.Details.Tags.Add("new");
            await session.Db.SaveChangesAsync();
        }

        await using var reader = harness.Open(widget.CompanyId);
        var stored = await reader.Db.Widgets.SingleAsync(w => w.Id == widget.Id);
        Assert.Multiple(() =>
        {
            Assert.That(stored.Details.Note, Is.EqualTo("changed"));
            Assert.That(stored.Details.Tags, Is.EqualTo(new[] { "red", "small", "new" }));
        });
    }

    [Test]
    public async Task New_keys_are_uuid_version_7_generated_by_the_application()
    {
        using var harness = new PersistenceHarness();
        var widget = Widgets.New(Guid.CreateVersion7(), "uuid");

        await using var session = harness.Open(widget.CompanyId);
        session.Db.Widgets.Add(widget);
        var idBeforeSaving = widget.Id;
        await session.Db.SaveChangesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(idBeforeSaving, Is.Not.EqualTo(Guid.Empty), "The key must exist before the INSERT.");
            Assert.That(widget.Id.Version, Is.EqualTo(7));
        });
    }
}
