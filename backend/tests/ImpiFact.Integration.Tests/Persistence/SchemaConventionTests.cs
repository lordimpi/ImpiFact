using Dapper;

namespace ImpiFact.Integration.Tests.Persistence;

/// <summary>
/// Revisa el esquema físico que crea PostgreSQL a partir del modelo: nombres en snake_case (minúsculas con
/// guion bajo), prefijos de llaves e índices, precisión de decimales y tipos de columna.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class SchemaConventionTests
{
    [Test]
    public async Task Tables_and_columns_use_snake_case()
    {
        var columns = await QueryAsync<string>(
            $"select column_name from information_schema.columns where table_schema = '{TestDatabase.DefaultSchema}' and table_name = 'widgets'");

        Assert.That(columns, Is.EquivalentTo(new[]
        {
            "id", "company_id", "group_id", "display_name", "price", "unit_price", "details", "occurred_at",
            "created_at", "updated_at", "row_version", "deleted_at",
        }));
    }

    [Test]
    public async Task Keys_and_indexes_use_the_prefixed_snake_case_names()
    {
        var indexes = await QueryAsync<string>(TestDatabase.IsSqlServer
            ? "select i.name from sys.indexes i where i.object_id = object_id('widgets') and i.type > 0"
            : TestDatabase.IsMySql
                ? "select distinct index_name from information_schema.statistics where table_schema = database() and table_name = 'widgets'"
                : "select indexname from pg_indexes where tablename = 'widgets'");

        // MySQL llama siempre PRIMARY a la llave primaria; los demás nombres son los de la convención.
        Assert.That(indexes, Is.EquivalentTo(new[]
        {
            TestDatabase.IsMySql ? "PRIMARY" : "pk_widgets", "ux_widgets_company_id_display_name", "ix_widgets_occurred_at",
            "ix_widgets_group_id",
        }));
    }

    [Test]
    public async Task Foreign_keys_are_named_after_child_and_parent_tables()
    {
        var foreignKeys = await QueryAsync<string>(
            "select constraint_name from information_schema.table_constraints " +
            "where table_name = 'widgets' and constraint_type = 'FOREIGN KEY'");

        Assert.That(foreignKeys, Is.EqualTo(new[] { "fk_widgets_widget_groups" }));
    }

    [Test]
    public async Task Decimals_have_explicit_precision_and_scale()
    {
        var precisions = await QueryAsync<(string Column, int Precision, int Scale)>(
            "select column_name, numeric_precision, numeric_scale from information_schema.columns " +
            "where table_name = 'widgets' and data_type in ('numeric', 'decimal') order by column_name");

        Assert.That(precisions, Is.EqualTo(new[] { ("price", 18, 2), ("unit_price", 18, 6) }));
    }

    // Los tipos de cada motor (Modelo de datos detallado, sección 8): jsonb, timestamptz y uuid en PostgreSQL;
    // nvarchar(max) con ISJSON, datetimeoffset y uniqueidentifier en SQL Server; json, datetime(6) en UTC (MySQL no tiene un
    // tipo con desfase) y char(36) en MySQL.
    [Test]
    public async Task Json_values_use_jsonb_and_instants_use_timestamptz()
    {
        var sqlServer = TestDatabase.IsSqlServer;
        var mysql = TestDatabase.IsMySql;
        var types = (await QueryAsync<(string Column, string Type)>(
                $"select column_name, {(mysql ? "column_type" : "data_type")} from information_schema.columns where table_name = 'widgets'"))
            .ToDictionary(column => column.Column, column => column.Type);

        Assert.Multiple(() =>
        {
            Assert.That(types["details"], Is.EqualTo(sqlServer ? "nvarchar" : mysql ? "json" : "jsonb"));
            Assert.That(types["occurred_at"], Is.EqualTo(sqlServer ? "datetimeoffset" : mysql ? "datetime(6)" : "timestamp with time zone"));
            Assert.That(types["deleted_at"], Is.EqualTo(sqlServer ? "datetimeoffset" : mysql ? "datetime(6)" : "timestamp with time zone"));
            Assert.That(types["row_version"], Is.EqualTo("bigint"));
            Assert.That(types["id"], Is.EqualTo(sqlServer ? "uniqueidentifier" : mysql ? "char(36)" : "uuid"));
        });
    }

    private static async Task<List<T>> QueryAsync<T>(string sql)
    {
        using var harness = new PersistenceHarness();
        await using var session = harness.Open(companyId: null);
        await using var connection = await session.Reads.OpenAsync(CancellationToken.None);
        return [.. await connection.QueryAsync<T>(sql)];
    }
}
