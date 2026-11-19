using System.Text.RegularExpressions;

namespace ImpiFact.Integration.Tests.Model;

/// <summary>
/// El esquema físico que crean las migraciones de MySQL frente al mismo modelo que <see cref="SchemaTests"/> revisa en
/// PostgreSQL (Modelo de datos detallado, sección 8 y criterio 2 de la sección 10), leído de <c>information_schema</c>.
/// Las expectativas son las de <see cref="SchemaTests"/> traducidas a los tipos de MySQL: uuid es <c>char(36)</c> en
/// <c>ascii</c>, ts es <c>datetime(6)</c>, bool es <c>tinyint(1)</c> y numeric es <c>decimal</c>.
/// </summary>
/// <remarks>
/// MySQL no tiene índices parciales: <c>ux_certificates_active</c> es único sobre la columna generada
/// <c>active_company_id</c>, y los índices con <c>where … IS NOT NULL</c> quedan sin filtro (un índice único de MySQL
/// acepta varios NULL). InnoDB además crea un índice por cada llave foránea que no tenga uno, con el nombre de la llave.
/// </remarks>
[TestFixture]
[Category("Integration")]
public sealed class MySqlSchemaTests
{
    private const string Schema = "table_schema = database()";

    /// <summary>Valores por defecto que fija el modelo, como los escribe MySQL.</summary>
    private static readonly string[] Defaults =
    [
        "type_documents.is_active 1", "companies.is_active 1", "companies.allow_seller_login 0",
        "companies.graphic_representation_template 2", "customers.is_active 1", "items.is_active 1",
        "resellers.is_active 1", "documents.prefix ", "documents.allowance_total_amount 0.00",
        "documents.charge_total_amount 0.00", "documents.prepaid_amount 0.00", "documents.email_status 0",
        "received_documents.prefix ", "document_files.revision 1", "plans.period 0",
        "plan_usage.used 0", "outbox_messages.status 0", "outbox_messages.attempts 0", "notifications.is_read 0",
    ];

    [SetUp]
    public void OnlyOnMySql()
    {
        if (!TestDatabase.IsMySql)
        {
            Assert.Ignore("MySQL only: SchemaTests checks the same model on PostgreSQL.");
        }
    }

    [Test]
    public async Task The_schema_has_exactly_the_model_tables()
    {
        var tables = await Sql.QueryAsync<string>($"select table_name from information_schema.tables where {Schema} and table_type = 'BASE TABLE'");

        Assert.That(tables.Except(SchemaTests.OtherModuleTables), Is.EquivalentTo(SchemaTests.ModelTables));
    }

    [TestCaseSource(typeof(SchemaTests), nameof(SchemaTests.ColumnTables))]
    public async Task Columns_match_the_data_model(string table)
    {
        var columns = await ColumnsAsync($"and table_name = '{table}'");

        Assert.That(columns.Where(column => !column.IsGenerated).Select(column => column.Describe()), Is.EquivalentTo(SchemaTests.Columns[table].Split('|')));
    }

    [Test]
    public async Task The_only_generated_column_backs_the_single_active_certificate()
    {
        var columns = await Sql.QueryAsync<(string Table, string Column, string Expression)>(
            $"select table_name, column_name, generation_expression from information_schema.columns where {Schema} and extra like '%GENERATED%'");

        Assert.That(columns, Is.EqualTo(new[] { ("certificates", "active_company_id", "(case when `is_active` then `company_id` end)") }));
    }

    [Test]
    public async Task Every_decimal_column_has_the_precision_of_its_kind()
    {
        var columns = await ColumnsAsync("and data_type = 'decimal'", qualified: true);

        Assert.That(columns.Select(column => column.Describe(withNullability: false)), Is.EquivalentTo(SchemaTests.Decimals));
    }

    [Test]
    public async Task Every_text_column_is_utf8mb4_with_case_and_accent_sensitive_collation()
    {
        var other = await Sql.QueryAsync<string>(
            $"select concat(table_name, '.', column_name, ' ', character_set_name, ' ', collation_name) from information_schema.columns " +
            $"where {Schema} and character_set_name is not null and table_name not in ('data_protection_keys', '__EFMigrationsHistory') " +
            "and not (character_set_name = 'utf8mb4' and collation_name = 'utf8mb4_0900_as_cs') " +
            "and not (character_set_name = 'ascii' and column_type = 'char(36)')");

        Assert.That(other, Is.Empty);
    }

    [Test]
    public async Task Composite_primary_keys_match_the_data_model()
    {
        var tables = SchemaTests.CompositePrimaryKeys.Select(key => key[..key.IndexOf('(', StringComparison.Ordinal)]).ToHashSet();
        var keys = (await IndexesAsync()).Where(index => index.Name == "PRIMARY" && tables.Contains(index.Table));

        Assert.That(keys.Select(key => key.Describe()), Is.EquivalentTo(SchemaTests.CompositePrimaryKeys));
    }

    [Test]
    public async Task Unique_constraints_match_the_data_model_and_none_is_declared_twice()
    {
        var unique = (await IndexesAsync()).Where(index => index.IsUnique && index.Name != "PRIMARY" && !SchemaTests.OtherModuleTables.Contains(index.Table));

        Assert.That(unique.Select(index => index.Describe()), Is.EquivalentTo(SchemaTests.UniqueConstraints.Select(WithoutFilter)));
    }

    [Test]
    public async Task Indexes_named_by_the_data_model_exist_with_their_definition()
    {
        var named = (await IndexesAsync()).Select(index => $"{index.Name}: {(index.IsUnique ? "unique " : "")}{index.Describe()}");

        Assert.That(named, Is.SupersetOf(SchemaTests.NamedIndexes.Select(WithoutFilter)));
    }

    [Test]
    public async Task Other_indexes_of_the_data_model_exist()
    {
        var indexes = (await IndexesAsync()).Where(index => !index.IsUnique);

        Assert.That(indexes.Select(index => index.Describe()), Is.SupersetOf(SchemaTests.Indexes));
    }

    [Test]
    public async Task Index_and_constraint_names_are_prefixed_unique_and_fit_in_63_characters()
    {
        var constraints = await Sql.QueryAsync<(string Table, string Name)>(
            $"select table_name, constraint_name from information_schema.table_constraints where {Schema} and constraint_type <> 'PRIMARY KEY'");
        // Un único es a la vez restricción e índice, y el índice que InnoDB crea para una llave foránea lleva su nombre.
        var indexes = (await IndexesAsync())
            .Where(index => index.Name != "PRIMARY" && !constraints.Contains((index.Table, index.Name)))
            .Select(index => (index.Table, index.Name));
        var names = constraints.Concat(indexes)
            .Where(item => !SchemaTests.OtherModuleTables.Contains(item.Table) || item.Table == "data_protection_keys")
            .Select(item => item.Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(names, Is.Unique);
            Assert.That(names.Where(name => name.Length > 63), Is.Empty);
            Assert.That(names.Where(name => !Regex.IsMatch(name, "^(ux|ix|fk|ck)_")), Is.Empty);
        });
    }

    [Test]
    public async Task Check_constraints_of_the_data_model_exist()
    {
        var checks = await Sql.QueryAsync<string>(
            $"select concat(table_name, '.', constraint_name) from information_schema.table_constraints where {Schema} and constraint_type = 'CHECK'");

        Assert.That(checks, Is.EquivalentTo(SchemaTests.CheckConstraints));
    }

    [Test]
    public async Task No_foreign_key_deletes_in_cascade()
    {
        var cascades = await Sql.QueryAsync<string>(
            "select constraint_name from information_schema.referential_constraints " +
            "where constraint_schema = database() and delete_rule in ('CASCADE', 'SET NULL', 'SET DEFAULT')");

        Assert.That(cascades, Is.Empty);
    }

    [Test]
    public async Task Tenant_references_are_composite_foreign_keys()
    {
        var foreignKeys = await Sql.QueryAsync<string>($"""
            select concat(table_name, '(', group_concat(column_name order by ordinal_position separator ','), ')->', referenced_table_name)
            from information_schema.key_column_usage
            where {Schema} and referenced_table_name is not null
            group by table_name, constraint_name, referenced_table_name
            having count(*) = 2
            """);

        Assert.That(foreignKeys, Is.EquivalentTo(new[]
        {
            "user_companies(company_id,customer_id)->customers",
            "documents(company_id,resolution_id)->resolutions", "documents(company_id,customer_id)->customers",
            "documents(company_id,reference_document_id)->documents",
            "document_lines(company_id,document_id)->documents", "document_taxes(company_id,document_id)->documents",
            "document_line_taxes(company_id,document_id)->documents", "document_line_taxes(company_id,line_id)->document_lines",
            "document_allowance_charges(company_id,document_id)->documents",
            "document_allowance_charges(company_id,line_id)->document_lines",
            "document_payments(company_id,document_id)->documents", "document_references(company_id,document_id)->documents",
            "document_files(company_id,document_id)->documents", "dian_submissions(company_id,document_id)->documents",
            "dian_submissions(company_id,request_file_id)->document_files",
            "dian_submissions(company_id,response_file_id)->document_files",
            "document_events(company_id,document_id)->documents",
            "document_events(company_id,received_document_id)->received_documents",
            "document_events(company_id,payload_file_id)->document_files",
        }));
    }

    [Test]
    public async Task Defaults_match_the_data_model()
    {
        var defaults = await Sql.QueryAsync<string>(
            $"select concat(table_name, '.', column_name, ' ', column_default) from information_schema.columns where {Schema} and column_default is not null");

        Assert.That(defaults, Is.SupersetOf(Defaults));
    }

    /// <summary>
    /// La expectativa de PostgreSQL sin su filtro: el único activo por empresa va sobre la columna generada y los demás
    /// filtros (<c>IS NOT NULL</c>) desaparecen.
    /// </summary>
    private static string WithoutFilter(string expected) => expected
        .Replace("certificates(company_id) where is_active", "certificates(active_company_id)", StringComparison.Ordinal)
        .Replace(" where (cufe IS NOT NULL)", "", StringComparison.Ordinal)
        .Replace(" where (track_id IS NOT NULL)", "", StringComparison.Ordinal);

    private static async Task<List<MySqlColumn>> ColumnsAsync(string filter, bool qualified = false) =>
        await Sql.QueryAsync<MySqlColumn>(
            $"select {(qualified ? "concat(table_name, '.', column_name)" : "column_name")} as Name, column_type as ColumnType, " +
            "character_set_name as CharSet, if(is_nullable = 'YES', 1, 0) as Nullable, extra as Extra " +
            $"from information_schema.columns where {Schema} {filter}");

    private static async Task<List<MySqlIndex>> IndexesAsync()
    {
        var columns = await Sql.QueryAsync<(string Table, string Name, long NonUnique, string Column, string? Collation)>(
            "select table_name, index_name, cast(non_unique as signed), column_name, collation from information_schema.statistics " +
            $"where {Schema} order by table_name, index_name, seq_in_index");
        return
        [
            .. columns.GroupBy(column => (column.Table, column.Name)).Select(group => new MySqlIndex(
                group.Key.Table, group.Key.Name, group.First().NonUnique == 0,
                string.Join(",", group.Select(column => column.Column + (column.Collation == "D" ? " desc" : ""))))),
        ];
    }

    /// <summary>Una columna de <c>information_schema.columns</c> descrita con los tipos lógicos del documento.</summary>
    private sealed class MySqlColumn
    {
        public string Name { get; init; } = "";

        public string ColumnType { get; init; } = "";

        public string? CharSet { get; init; }

        public long Nullable { get; init; }

        public string Extra { get; init; } = "";

        public bool IsGenerated => Extra.Contains("GENERATED", StringComparison.OrdinalIgnoreCase);

        public string Describe(bool withNullability = true) => $"{Name} {Type()}{(withNullability && Nullable == 1 ? " null" : "")}";

        private string Type() => ColumnType switch
        {
            "char(36)" when CharSet == "ascii" => "uuid",
            "datetime(6)" => "ts",
            "tinyint(1)" => "bool",
            // TimeOnly queda en time(6), con microsegundos como el time de PostgreSQL.
            "time(6)" => "time",
            var type when type.StartsWith("decimal(", StringComparison.Ordinal) => "numeric" + type["decimal".Length..],
            _ => ColumnType,
        };
    }

    /// <summary>Un índice de <c>information_schema.statistics</c>, descrito como <c>tabla(columnas)</c>.</summary>
    private sealed record MySqlIndex(string Table, string Name, bool IsUnique, string Columns)
    {
        public string Describe() => $"{Table}({Columns})";
    }
}
