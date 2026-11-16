using Dapper;

namespace ImpiFact.Integration.Tests.Persistence;

/// <summary>
/// Las consultas de lectura van con Dapper (una librería que ejecuta SQL y llena objetos) sobre una conexión
/// que entrega <c>IReadConnectionFactory</c>. Los filtros de EF no aplican aquí: el SQL lleva el company_id.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class ReadConnectionTests
{
    [Test]
    public async Task Dapper_reads_rows_through_the_read_connection_factory()
    {
        using var harness = new PersistenceHarness();
        var companyId = Guid.CreateVersion7();
        await Widgets.SaveAsync(harness, Widgets.New(companyId, "read one"));
        await Widgets.SaveAsync(harness, Widgets.New(companyId, "read two"));
        await Widgets.SaveAsync(harness, Widgets.New(Guid.CreateVersion7(), "read other company"));

        await using var session = harness.Open(companyId);
        await using var connection = await session.Reads.OpenAsync(CancellationToken.None);
        var names = await connection.QueryAsync<string>(
            "select display_name from widgets where company_id = @CompanyId order by display_name",
            new { CompanyId = companyId });

        Assert.That(names, Is.EqualTo(new[] { "read one", "read two" }));
    }
}
