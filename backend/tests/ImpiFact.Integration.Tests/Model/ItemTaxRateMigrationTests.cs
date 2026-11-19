using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ImpiFact.Integration.Tests.Model;

[Category("Integration")]
public sealed class ItemTaxRateMigrationTests
{
    private static async Task<List<string>> ColumnsAsync(DbContext db)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "select column_name from information_schema.columns where table_name = 'items' and column_name like 'tax_%' order by 1";
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    [Test]
    public async Task ItemTaxRate_down_removes_only_its_two_columns_and_up_restores_them()
    {
        // ItemTaxRate es una migración de PostgreSQL: MySQL empezó con una sola migración inicial que ya trae esas columnas.
        TestDatabase.RequirePostgres("ItemTaxRate is a PostgreSQL migration; MySQL starts from one InitialSchema.");
        await using var session = ModelDatabase.Open(companyId: null);
        var migrator = session.Db.GetService<IMigrator>();

        var up = await ColumnsAsync(session.Db);
        await migrator.MigrateAsync("20261116145207_InitialSchema");
        var down = await ColumnsAsync(session.Db);
        await migrator.MigrateAsync();
        var again = await ColumnsAsync(session.Db);

        Assert.Multiple(() =>
        {
            Assert.That(up, Is.EqualTo(new[] { "tax_id", "tax_per_unit_amount", "tax_percent" }));
            Assert.That(down, Is.EqualTo(new[] { "tax_id" }));
            Assert.That(again, Is.EqualTo(up));
        });
    }
}
