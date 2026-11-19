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
            "union all select 'tables ' || count(*) from information_schema.tables where table_schema = 'public' order by 1";
        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        return lines;
    }

    [Test]
    public async Task PasswordResetTokens_down_removes_only_its_table_and_up_restores_it()
    {
        // PasswordResetTokens es una migración de PostgreSQL (y la consulta lee pg_indexes): MySQL empezó con una sola
        // migración inicial que ya trae la tabla.
        TestDatabase.RequirePostgres("PasswordResetTokens is a PostgreSQL migration; MySQL starts from one InitialSchema.");
        await using var session = ModelDatabase.Open(companyId: null);
        var migrator = session.Db.GetService<IMigrator>();

        var up = await DescribeAsync(session.Db);
        await migrator.MigrateAsync("20261117214651_ItemTaxRate");
        var down = await DescribeAsync(session.Db);
        await migrator.MigrateAsync();
        var again = await DescribeAsync(session.Db);

        var tablesUp = int.Parse(up.Single(line => line.StartsWith("tables ", StringComparison.Ordinal))[7..], System.Globalization.CultureInfo.InvariantCulture);
        Assert.Multiple(() =>
        {
            Assert.That(up, Does.Contain("column token_hash character"));
            Assert.That(up, Does.Contain("column used_at timestamp with time zone"));
            Assert.That(up, Does.Contain("column revoked_at timestamp with time zone"));
            Assert.That(up, Does.Contain("index ux_password_reset_tokens_token_hash"));
            Assert.That(up, Does.Contain("index ix_password_reset_tokens_user_id"));
            Assert.That(down, Is.EqualTo(new[] { $"tables {tablesUp - 1}" }), "Only the new table goes away.");
            Assert.That(again, Is.EqualTo(up));
        });
    }
}
