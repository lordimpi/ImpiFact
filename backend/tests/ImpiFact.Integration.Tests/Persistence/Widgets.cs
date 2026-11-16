namespace ImpiFact.Integration.Tests.Persistence;

/// <summary>Ayudas para crear y guardar entidades de prueba.</summary>
internal static class Widgets
{
    public static Widget New(Guid companyId, string name) => new()
    {
        CompanyId = companyId,
        DisplayName = name,
        Price = 10.25m,
        UnitPrice = 1.123456m,
        Details = new WidgetDetails { Tags = ["red", "small"], Note = "note" },
        OccurredAt = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero),
    };

    /// <summary>Guarda la entidad en un ámbito propio de su empresa.</summary>
    public static async Task SaveAsync(PersistenceHarness harness, Widget widget)
    {
        await using var session = harness.Open(widget.CompanyId);
        session.Db.Widgets.Add(widget);
        await session.Db.SaveChangesAsync();
    }
}
