using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ImpiFact.Integration.Tests.Model;

/// <summary>
/// La migración <c>PasswordResetTokens</c> solo agrega la tabla <c>password_reset_tokens</c> con sus dos índices: al
/// revertirla desaparece sin tocar nada más y al aplicarla de nuevo vuelve igual.
/// </summary>
[Category("Integration")]
public sealed class PasswordResetTokensMigrationTests
{
    private static async Task<List<string>> DescribeAsync(DbContext db)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            "select 'column ' || column_name || ' ' || data_type from information_schema.columns where table_name = 'password_reset_tokens' " +
            "union all select 'index ' || indexname from pg_indexes where tablename = 'password_reset_tokens' " +
            "union all select 'table ' || table_name from information_schema.tables where table_schema = 'public' and table_type = 'BASE TABLE' order by 1";
        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        return lines;
    }

    /// <summary>Tables created by migrations after PasswordResetTokens, which the revert also removes.</summary>
    private static readonly string[] LaterTables = ["table document_batches"];

    private static IEnumerable<string> PasswordResetTokensShape(IEnumerable<string> lines) =>
        lines.Where(line => !line.StartsWith("table ", StringComparison.Ordinal));

    [Test]
    public async Task PasswordResetTokens_down_removes_only_its_table_and_up_restores_it()
    {
        // Solo PostgreSQL tiene la migración PasswordResetTokens aparte: los demás motores empiezan con una sola InitialSchema que ya
        // trae el cambio, así que no hay migración que revertir.
        Assume.That(TestDatabase.IsPostgres, "PasswordResetTokens is a PostgreSQL-only migration; SQL Server and MySQL start from one InitialSchema.");
        await using var session = ModelDatabase.Open(companyId: null);
        var migrator = session.Db.GetService<IMigrator>();

        var up = await DescribeAsync(session.Db);
        await migrator.MigrateAsync("20261117214651_ItemTaxRate");
        var down = await DescribeAsync(session.Db);
        await migrator.MigrateAsync();
        var again = await DescribeAsync(session.Db);

        Assert.Multiple(() =>
        {
            Assert.That(up, Does.Contain("column token_hash character"));
            Assert.That(up, Does.Contain("column used_at timestamp with time zone"));
            Assert.That(up, Does.Contain("column revoked_at timestamp with time zone"));
            Assert.That(up, Does.Contain("index ux_password_reset_tokens_token_hash"));
            Assert.That(up, Does.Contain("index ix_password_reset_tokens_user_id"));
            // Going back to ItemTaxRate also reverts the later migrations; of those, only DocumentBatches creates a table.
            Assert.That(up.Except(down), Is.EquivalentTo(LaterTables.Append("table password_reset_tokens").Concat(PasswordResetTokensShape(up))),
                "Only the new table (and the tables of later migrations) go away.");
            Assert.That(down.Except(up), Is.Empty, "Reverting adds nothing.");
            Assert.That(again, Is.EqualTo(up));
        });
    }
}
