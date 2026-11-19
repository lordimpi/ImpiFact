using Dapper;

namespace ImpiFact.Integration.Tests.Persistence;

/// <summary>
/// Revisa el esquema físico que crea el motor de las pruebas (PostgreSQL o MySQL) a partir del modelo: nombres en snake_case (minúsculas con
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
            $"select column_name from information_schema.columns where table_schema = {TestSql.CurrentSchema} and table_name = 'widgets'");

        Assert.That(columns, Is.EquivalentTo(new[]
        {
            "id", "company_id", "group_id", "display_name", "price", "unit_price", "details", "occurred_at",
            "created_at", "updated_at", "row_version", "deleted_at",
        }));
    }

    [Test]
    public async Task Keys_and_indexes_use_the_prefixed_snake_case_names()
    {
        // MySQL llama siempre PRIMARY a la llave primaria; el resto de los nombres es el mismo.
        var indexes = await QueryAsync<string>(TestDatabase.IsMySql
            ? "select distinct index_name from information_schema.statistics where table_schema = database() and table_name = 'widgets'"
            : "select indexname from pg_indexes where tablename = 'widgets'");

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
            $"where table_schema = {TestSql.CurrentSchema} and table_name = 'widgets' and constraint_type = 'FOREIGN KEY'");

        Assert.That(foreignKeys, Is.EqualTo(new[] { "fk_widgets_widget_groups" }));
    }

    [Test]
    public async Task Decimals_have_explicit_precision_and_scale()
    {
        // information_schema da la precisión como integer en PostgreSQL y bigint unsigned en MySQL: se lee como texto.
        var rows = await QueryAsync<(string Column, string Precision, string Scale)>(
            $"select column_name, {TestSql.Text("numeric_precision")}, {TestSql.Text("numeric_scale")} from information_schema.columns " +
            $"where table_schema = {TestSql.CurrentSchema} and table_name = 'widgets' and data_type in ('numeric', 'decimal') order by column_name");

        var precisions = rows.Select(row => (row.Column, int.Parse(row.Precision, System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(row.Scale, System.Globalization.CultureInfo.InvariantCulture)));

        Assert.That(precisions, Is.EqualTo(new[] { ("price", 18, 2), ("unit_price", 18, 6) }));
    }

    [Test]
    public async Task Json_values_use_jsonb_and_instants_use_timestamptz()
    {
        TestDatabase.RequirePostgres("checks PostgreSQL types; the MySQL types are checked by the next test.");
        var types = await TypesAsync("data_type");

        Assert.Multiple(() =>
        {
            Assert.That(types["details"], Is.EqualTo("jsonb"));
            Assert.That(types["occurred_at"], Is.EqualTo("timestamp with time zone"));
            Assert.That(types["deleted_at"], Is.EqualTo("timestamp with time zone"));
            Assert.That(types["row_version"], Is.EqualTo("bigint"));
            Assert.That(types["id"], Is.EqualTo("uuid"));
        });
    }

    // MySQL no tiene un tipo con desfase: los instantes son datetime(6) en UTC; los uuid, char(36) en ascii.
    [Test]
    public async Task On_mysql_json_values_use_json_and_instants_use_datetime_in_utc()
    {
        if (!TestDatabase.IsMySql)
        {
            Assert.Ignore("MySQL only: the previous test checks the PostgreSQL types.");
        }

        var types = await TypesAsync("column_type");

        Assert.Multiple(() =>
        {
            Assert.That(types["details"], Is.EqualTo("json"));
            Assert.That(types["occurred_at"], Is.EqualTo("datetime(6)"));
            Assert.That(types["deleted_at"], Is.EqualTo("datetime(6)"));
            Assert.That(types["row_version"], Is.EqualTo("bigint"));
            Assert.That(types["id"], Is.EqualTo("char(36)"));
        });
    }

    private static async Task<Dictionary<string, string>> TypesAsync(string typeColumn) =>
        (await QueryAsync<(string Column, string Type)>(
            $"select column_name, {typeColumn} from information_schema.columns where table_schema = {TestSql.CurrentSchema} and table_name = 'widgets'"))
        .ToDictionary(column => column.Column, column => column.Type);

    private static async Task<List<T>> QueryAsync<T>(string sql)
    {
        using var harness = new PersistenceHarness();
        await using var session = harness.Open(companyId: null);
        await using var connection = await session.Reads.OpenAsync(CancellationToken.None);
        return [.. await connection.QueryAsync<T>(sql)];
    }
}
