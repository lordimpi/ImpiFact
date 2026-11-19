namespace ImpiFact.Integration.Tests.Model;

/// <summary>
/// Compara el esquema físico que crea el motor de las pruebas (PostgreSQL o SQL Server) con «Modelo de datos
/// detallado» (secciones 2, 3, 6, 7 y 8; criterio 2 de la sección 11): tablas, columnas con tipo y nulabilidad,
/// precisión de cada decimal, llaves, únicos, índices, restricciones de verificación y llaves foráneas.
/// </summary>
/// <remarks>
/// Las columnas se escriben como en el documento: <c>nombre tipo [null]</c>, con los tipos lógicos de su sección 2
/// (<c>ts</c> es un instante UTC, <c>json</c> es <c>jsonb</c> en PostgreSQL y <c>nvarchar(max)</c> con <c>ISJSON</c> en SQL
/// Server, <c>varchar</c> es <c>nvarchar</c> en SQL Server). Cada motor lee su catálogo con su <see cref="Catalog"/>.
/// </remarks>
[TestFixture]
[Category("Integration")]
public sealed partial class SchemaTests
{
    private const string Common = "created_at ts|updated_at ts|row_version bigint";

    private static readonly string[] ModelTables =
    [
        .. CatalogSeedTests.CatalogTables,
        "resellers", "users", "roles", "user_roles", "user_companies", "api_keys", "refresh_tokens", "password_reset_tokens",
        "companies", "company_liabilities", "company_environments", "company_software", "certificates", "resolutions",
        "company_mail_settings", "company_logos",
        "customers", "items",
        "documents", "document_lines", "document_taxes", "document_line_taxes", "document_allowance_charges",
        "document_payments", "document_references", "document_files", "dian_submissions", "document_events",
        "received_documents", "numbering_sequences",
        "plans", "plan_quotas", "company_plans", "plan_usage",
        "outbox_messages", "audit_logs", "notifications", "idempotency_keys",
    ];

    /// <summary>Tablas que mapea otro módulo en el mismo contexto (Data Protection) y que esta prueba no describe.</summary>
    // data_protection_keys es de Protection; __EFMigrationsHistory es la tabla donde EF anota las migraciones aplicadas.
    private static readonly string[] OtherModuleTables = ["data_protection_keys", "__EFMigrationsHistory"];

    private static readonly Dictionary<string, string> Columns = new(StringComparer.Ordinal)
    {
        ["type_documents"] = "id int|code varchar(20)|name varchar(255)|is_active bool|valid_from date null|valid_to date null|" +
            "cufe_algorithm varchar(16) null|prefix varchar(10) null",
        ["taxes"] = "id int|code varchar(20)|name varchar(255)|is_active bool|valid_from date null|valid_to date null|description varchar(255)",
        ["countries"] = "id int|code varchar(5)|name varchar(255)|is_active bool|valid_from date null|valid_to date null",
        ["municipalities"] = "id int|code varchar(10)|name varchar(255)|is_active bool|valid_from date null|valid_to date null|" +
            "department_id int|codefacturador bigint null",
        ["type_item_identifications"] = "id int|code varchar(20)|name varchar(255)|is_active bool|valid_from date null|valid_to date null|" +
            "code_agency varchar(10) null",
        ["users"] = "id uuid|email varchar(255)|password_hash varchar(500)|name varchar(255)|phone varchar(30) null|" +
            "reseller_id uuid null|is_active bool|email_confirmed bool|lockout_end ts null|access_failed_count int|" +
            "security_stamp varchar(100)|" + Common,
        ["api_keys"] = "id uuid|company_id uuid null|name varchar(100)|key_prefix varchar(12)|key_hash char(64)|scopes varchar(500)|" +
            "expires_at ts null|last_used_at ts null|revoked_at ts null|created_by uuid null|created_at ts|updated_at ts",
        ["refresh_tokens"] = "id uuid|user_id uuid|token_hash char(64)|expires_at ts|revoked_at ts null|replaced_by uuid null|" +
            "created_ip varchar(45) null|user_agent varchar(300) null|" + Common,
        ["password_reset_tokens"] = "id uuid|user_id uuid|token_hash char(64)|expires_at ts|used_at ts null|revoked_at ts null|" +
            "requested_ip varchar(45) null|" + Common,
        ["companies"] = "id uuid|reseller_id uuid null|identification_number varchar(20)|dv char(1)|name varchar(255)|" +
            "email varchar(255)|phone varchar(30)|address varchar(255)|merchant_registration varchar(50) null|language_id int|" +
            "country_id int|municipality_id int|tax_id int|type_currency_id int|type_operation_id int|" +
            "type_document_identification_id int|type_organization_id int|type_regime_id int|storage_mode smallint null|" +
            "graphic_representation_template int|allow_seller_login bool|is_active bool|" + Common,
        ["company_software"] = "id uuid|company_id uuid|document_kind smallint|type_environment_id int|software_identifier varchar(64)|" +
            "pin_cipher varchar(512)|dian_url varchar(255)|test_set_id varchar(64) null|" + Common,
        ["certificates"] = "id uuid|company_id uuid|name varchar(255)|secret_ref varchar(512)|thumbprint varchar(64)|" +
            "subject varchar(500)|issuer varchar(500)|not_before ts|not_after ts|is_active bool|uploaded_by uuid null|" + Common,
        ["resolutions"] = "id uuid|company_id uuid|document_type_id int|type_environment_id int|prefix varchar(10) null|" +
            "resolution_number varchar(50) null|resolution_date date null|technical_key varchar(128) null|range_from bigint|range_to bigint|" +
            "valid_from date null|valid_to date null|is_active bool|" + Common,
        ["company_mail_settings"] = "company_id uuid|use_platform_default bool|host varchar(255) null|port int null|" +
            "username varchar(255) null|password_cipher varchar(512) null|encryption varchar(10) null|" +
            "from_address varchar(255) null|from_name varchar(255) null|" + Common,
        ["customers"] = "id uuid|company_id uuid|identification_number varchar(20)|dv char(1) null|type_document_identification_id int|" +
            "name varchar(255)|phone varchar(30) null|address varchar(255) null|email varchar(255) null|municipality_id int null|" +
            "type_organization_id int null|type_regime_id int null|type_liability_id int null|merchant_registration varchar(50) null|" +
            "is_active bool|deleted_at ts null|" + Common,
        ["items"] = "id uuid|company_id uuid|code varchar(100)|name varchar(500)|description varchar(1000) null|unit_measure_id int|" +
            "type_item_identification_id int|sale_unit_price numeric(18,6)|tax_id int null|tax_percent numeric(9,4) null|" +
            "tax_per_unit_amount numeric(18,6) null|is_active bool|deleted_at ts null|" + Common,
        ["documents"] = "id uuid|company_id uuid|document_type_id int|type_environment_id int|resolution_id uuid null|" +
            "prefix varchar(10)|number bigint|full_number varchar(30)|status smallint|cufe varchar(96) null|" +
            "uuid_algorithm varchar(16) null|issue_date date|issue_time time|issued_at ts|customer_id uuid null|" +
            "customer_snapshot json|currency_id int|exchange_rate numeric(18,6) null|type_operation_id int null|" +
            "line_extension_amount numeric(18,2)|tax_exclusive_amount numeric(18,2)|tax_inclusive_amount numeric(18,2)|" +
            "allowance_total_amount numeric(18,2)|charge_total_amount numeric(18,2)|prepaid_amount numeric(18,2)|" +
            "payable_amount numeric(18,2)|reference_document_id uuid null|discrepancy_code int null|" +
            "discrepancy_description varchar(500) null|notes varchar(2000) null|head_note varchar(2000) null|" +
            "foot_note varchar(2000) null|track_id varchar(64) null|dian_status_code varchar(10) null|" +
            "dian_status_description varchar(500) null|sent_at ts null|accepted_at ts null|email_status smallint|" +
            "email_sent_at ts null|pdf_template_id int null|extensions json null|created_by_type smallint|" +
            "created_by_id uuid null|" + Common,
        ["document_lines"] = "id uuid|company_id uuid|document_id uuid|line_number int|code varchar(100) null|description varchar(500)|" +
            "notes varchar(1000) null|type_item_identification_id int|unit_measure_id int|quantity numeric(18,6)|" +
            "base_quantity numeric(18,6)|unit_price numeric(18,6)|line_extension_amount numeric(18,2)|free_of_charge bool|" +
            "reference_price_id int null|extensions json null|" + Common,
        ["document_files"] = "id uuid|company_id uuid|document_id uuid|kind smallint|revision int|storage_provider smallint|" +
            "storage_key varchar(500)|content_type varchar(100)|size_bytes bigint|sha256 char(64)|" + Common,
        ["dian_submissions"] = "id uuid|company_id uuid|document_id uuid|attempt int|operation smallint|type_environment_id int|" +
            "track_id varchar(64) null|zip_key varchar(64) null|http_status int null|dian_status_code varchar(10) null|" +
            "dian_status_description varchar(500) null|is_valid bool null|error_messages json null|request_file_id uuid null|" +
            "response_file_id uuid null|started_at ts|finished_at ts null|duration_ms int null|" + Common,
        ["document_events"] = "id uuid|company_id uuid|document_id uuid null|received_document_id uuid null|event_id int|" +
            "cude varchar(96)|issued_at ts|track_id varchar(64) null|status smallint|dian_status_code varchar(10) null|" +
            "payload_file_id uuid null|" + Common,
        ["received_documents"] = "id uuid|company_id uuid|supplier_identification_number varchar(20)|supplier_dv char(1)|" +
            "supplier_name varchar(255)|document_type_id int|prefix varchar(10)|number varchar(20)|cufe varchar(96)|issue_date date|" +
            "type_environment_id int|line_extension_amount numeric(18,2)|tax_exclusive_amount numeric(18,2)|" +
            "allowance_total_amount numeric(18,2)|tax_amount numeric(18,2)|payable_amount numeric(18,2)|" +
            "xml_file_key varchar(500) null|pdf_file_key varchar(500) null|" + Common,
        ["numbering_sequences"] = "id uuid|company_id uuid|scope_key varchar(120)|next_number bigint|" + Common,
        ["plans"] = "id int|name varchar(100)|period smallint|is_active bool|observations varchar(500) null|" + Common,
        ["plan_usage"] = "company_plan_id uuid|document_kind smallint|used bigint|row_version bigint",
        ["outbox_messages"] = "id uuid|company_id uuid null|type varchar(100)|payload json|status smallint|attempts int|" +
            "next_attempt_at ts|locked_by varchar(100) null|locked_until ts null|last_error varchar(2000) null|created_at ts|" +
            "processed_at ts null",
        ["audit_logs"] = "id bigint|company_id uuid null|actor_type smallint|actor_id uuid null|action varchar(100)|" +
            "entity_type varchar(100)|entity_id varchar(64) null|correlation_id varchar(64) null|ip varchar(45) null|" +
            "user_agent varchar(300) null|changes json null|created_at ts",
        ["idempotency_keys"] = "id uuid|company_id uuid|key varchar(100)|request_hash char(64)|response_status int|" +
            "response_body json null|expires_at ts|" + Common,
    };

    /// <summary>Todas las columnas decimales del modelo: importes (18,2), precios y cantidades (18,6), porcentajes (9,4).</summary>
    private static readonly string[] Decimals =
    [
        "documents.exchange_rate numeric(18,6)", "documents.line_extension_amount numeric(18,2)",
        "documents.tax_exclusive_amount numeric(18,2)", "documents.tax_inclusive_amount numeric(18,2)",
        "documents.allowance_total_amount numeric(18,2)", "documents.charge_total_amount numeric(18,2)",
        "documents.prepaid_amount numeric(18,2)", "documents.payable_amount numeric(18,2)",
        "document_lines.quantity numeric(18,6)", "document_lines.base_quantity numeric(18,6)",
        "document_lines.unit_price numeric(18,6)", "document_lines.line_extension_amount numeric(18,2)",
        "document_taxes.percent numeric(9,4)", "document_taxes.taxable_amount numeric(18,2)", "document_taxes.tax_amount numeric(18,2)",
        "document_line_taxes.percent numeric(9,4)", "document_line_taxes.taxable_amount numeric(18,2)",
        "document_line_taxes.tax_amount numeric(18,2)", "document_line_taxes.per_unit_amount numeric(18,6)",
        "document_allowance_charges.amount numeric(18,2)", "document_allowance_charges.base_amount numeric(18,2)",
        "document_allowance_charges.multiplier_factor numeric(9,4)",
        "received_documents.line_extension_amount numeric(18,2)", "received_documents.tax_exclusive_amount numeric(18,2)",
        "received_documents.allowance_total_amount numeric(18,2)", "received_documents.tax_amount numeric(18,2)",
        "received_documents.payable_amount numeric(18,2)",
        "items.sale_unit_price numeric(18,6)", "items.tax_percent numeric(9,4)", "items.tax_per_unit_amount numeric(18,6)",
    ];

    /// <summary>Llaves primarias que no son <c>id</c>.</summary>
    private static readonly string[] CompositePrimaryKeys =
    [
        "user_roles(user_id,role_id)", "company_liabilities(company_id,type_liability_id)",
        "plan_quotas(plan_id,document_kind)", "plan_usage(company_plan_id,document_kind)",
        "company_mail_settings(company_id)", "company_logos(company_id)",
    ];

    /// <summary>
    /// Todos los únicos (UQ) del modelo, salvo las llaves primarias: <c>tabla(columnas) [where filtro]</c>. Cada
    /// uno aparece una sola vez: un único declarado dos veces (llave alterna e índice) haría fallar la prueba.
    /// </summary>
    private static readonly string[] UniqueConstraints =
    [
        .. CatalogSeedTests.CatalogTables
            .Except(["countries", "languages", "unit_measures", "type_operations", "municipalities"])
            .Select(table => $"{table}(code)"),
        "municipalities(department_id,code)",
        "resellers(identification_number)", "resellers(email)", "users(email)", "roles(name)",
        "user_companies(user_id,company_id,role_id)", "api_keys(key_hash)", "refresh_tokens(token_hash)",
        "password_reset_tokens(token_hash)", "companies(identification_number)", "company_environments(company_id,document_kind)",
        "company_software(company_id,document_kind,type_environment_id)", "certificates(company_id) where is_active",
        "resolutions(company_id,id)",
        "customers(company_id,id)", "customers(company_id,type_document_identification_id,identification_number)",
        "items(company_id,code)",
        "documents(company_id,document_type_id,type_environment_id,prefix,number)",
        "documents(company_id,cufe) where (cufe IS NOT NULL)", "documents(company_id,id)",
        "document_lines(company_id,id)", "document_lines(document_id,line_number)",
        "document_files(company_id,id)", "document_files(document_id,kind,revision)",
        "dian_submissions(document_id,attempt)", "document_events(company_id,cude)",
        "received_documents(company_id,id)", "received_documents(company_id,cufe)",
        "numbering_sequences(company_id,scope_key)", "plans(name)", "idempotency_keys(company_id,key)",
    ];

    /// <summary>Índices con el nombre que fija el modelo (secciones 3.3, 3.5 y 6): <c>nombre: definición</c>.</summary>
    private static readonly string[] NamedIndexes =
    [
        "ux_certificates_active: unique certificates(company_id) where is_active",
        "ix_certificates_not_after: certificates(not_after)",
        "ux_documents_number: unique documents(company_id,document_type_id,type_environment_id,prefix,number)",
        "ux_documents_cufe: unique documents(company_id,cufe) where (cufe IS NOT NULL)",
        "ix_documents_company_issued: documents(company_id,issued_at desc)",
        "ix_documents_company_status: documents(company_id,status)",
        "ix_documents_customer: documents(company_id,customer_id,issued_at desc)",
        "ix_documents_track: documents(track_id) where (track_id IS NOT NULL)",
    ];

    /// <summary>Los demás índices (IX) que pide el modelo, sin nombre propio.</summary>
    private static readonly string[] Indexes =
    [
        "countries(code)", "languages(code)", "unit_measures(code)", "type_operations(code)",
        "api_keys(company_id,revoked_at)",
        "resolutions(company_id,document_type_id,type_environment_id,is_active)",
        "document_taxes(document_id)", "document_line_taxes(line_id)", "dian_submissions(track_id)",
        "document_events(document_id)", "document_events(received_document_id)",
        "received_documents(company_id,issue_date desc)", "received_documents(company_id,supplier_identification_number)",
        "company_plans(company_id,is_active)", "outbox_messages(status,next_attempt_at)",
        "audit_logs(company_id,created_at desc)", "audit_logs(entity_type,entity_id)",
        "notifications(company_id,user_id,is_read,created_at desc)", "idempotency_keys(expires_at)",
    ];

    private static readonly string[] CheckConstraints =
    [
        "resolutions.ck_resolutions_range", "document_events.ck_document_events_target",
    ];

    /// <summary>Consultas al catálogo del motor con que corren las pruebas.</summary>
    private static readonly Catalog Schema = TestDatabase.IsSqlServer ? new SqlServerCatalog() : new PostgresCatalog();

    [Test]
    public async Task The_schema_has_exactly_the_model_tables()
    {
        var tables = await Sql.QueryAsync<string>(
            "select table_name from information_schema.tables where table_schema = @schema and table_type = 'BASE TABLE'",
            new { schema = TestDatabase.DefaultSchema });

        Assert.That(tables.Except(OtherModuleTables), Is.EquivalentTo(ModelTables));
    }

    [TestCaseSource(nameof(ColumnTables))]
    public async Task Columns_match_the_data_model(string table)
    {
        var columns = await Sql.QueryAsync<ColumnInfo>(Schema.ColumnsQuery + " and table_name = @table", new { table });

        Assert.That(columns.Select(column => column.Describe()), Is.EquivalentTo(Columns[table].Split('|')));
    }

    [Test]
    public async Task Every_decimal_column_has_the_precision_of_its_kind()
    {
        var columns = await Sql.QueryAsync<ColumnInfo>(Schema.ColumnsQuery + " and data_type in ('numeric', 'decimal')");

        Assert.That(columns.Select(column => $"{column.Table}.{(column with { IsNullable = false }).Describe()}"), Is.EquivalentTo(Decimals));
    }

    [Test]
    public async Task Every_table_with_company_id_has_the_common_tenant_columns()
    {
        var columns = await Sql.QueryAsync<ColumnInfo>(Schema.ColumnsQuery);
        var byTable = columns.GroupBy(column => column.Table).ToDictionary(group => group.Key, group => group.ToList());
        var platformTables = new[] { "api_keys", "outbox_messages", "audit_logs" };

        var missing = byTable
            .Where(table => table.Value.Any(column => column.Name == "company_id") && !platformTables.Contains(table.Key))
            .SelectMany(table => new[] { "company_id", "created_at", "updated_at", "row_version" }
                .Where(required => !table.Value.Any(column => column.Name == required && !column.IsNullable))
                .Select(required => $"{table.Key}.{required}"));

        Assert.That(missing, Is.Empty);
    }

    [Test]
    public async Task Composite_primary_keys_match_the_data_model()
    {
        var tables = CompositePrimaryKeys.Select(key => key[..key.IndexOf('(', StringComparison.Ordinal)]).ToHashSet(StringComparer.Ordinal);
        var keys = (await Schema.IndexesAsync()).Where(index => index.IsPrimary && tables.Contains(index.Table));

        Assert.That(keys.Select(key => key.Describe()), Is.EquivalentTo(CompositePrimaryKeys));
    }

    [Test]
    public async Task Unique_constraints_match_the_data_model_and_none_is_declared_twice()
    {
        var unique = (await Schema.IndexesAsync()).Where(index => index.IsUnique && !index.IsPrimary);
        var modelUnique = unique.Where(index => !OtherModuleTables.Contains(index.Table)).Select(index => index.Describe());

        Assert.That(modelUnique, Is.EquivalentTo(UniqueConstraints));
    }

    [Test]
    public async Task Indexes_named_by_the_data_model_exist_with_their_definition()
    {
        var indexes = await Schema.IndexesAsync();
        var named = indexes.Select(index => $"{index.Name}: {(index.IsUnique ? "unique " : "")}{index.Describe()}");

        Assert.That(named, Is.SupersetOf(NamedIndexes));
    }

    [Test]
    public async Task Other_indexes_of_the_data_model_exist()
    {
        var indexes = (await Schema.IndexesAsync()).Where(index => !index.IsUnique);

        Assert.That(indexes.Select(index => index.Describe()), Is.SupersetOf(Indexes));
    }

    [Test]
    public async Task Index_and_constraint_names_are_prefixed_unique_and_fit_in_63_characters()
    {
        var names = await Sql.QueryAsync<string>(Schema.ConstraintAndIndexNamesQuery);

        // La llave primaria de la tabla de historial de EF la nombra EF, no la convención de ImpiFact.
        names = names.Where(name => name != "PK___EFMigrationsHistory").ToList();

        Assert.Multiple(() =>
        {
            Assert.That(names, Is.Unique);
            Assert.That(names.Where(name => name.Length > 63), Is.Empty);
            Assert.That(names.Where(name => !name.StartsWith("pk_", StringComparison.Ordinal) && !name.StartsWith("ux_", StringComparison.Ordinal)
                && !name.StartsWith("ix_", StringComparison.Ordinal) && !name.StartsWith("fk_", StringComparison.Ordinal)
                && !name.StartsWith("ck_", StringComparison.Ordinal)), Is.Empty);
        });
    }

    // SQL Server además valida cada columna JSON con ISJSON (no tiene tipo JSON): una restricción por columna json del modelo.
    [Test]
    public async Task Check_constraints_of_the_data_model_exist()
    {
        var checks = await Sql.QueryAsync<string>(Schema.CheckConstraintsQuery);
        var jsonChecks = TestDatabase.IsSqlServer
            ? Columns.SelectMany(table => table.Value.Split('|')
                .Select(column => column.Split(' '))
                .Where(column => column[1] == "json")
                .Select(column => $"{table.Key}.ck_{table.Key}_{column[0]}_json"))
            : [];

        Assert.That(checks, Is.EquivalentTo(CheckConstraints.Concat(jsonChecks)));
    }

    [Test]
    public async Task No_foreign_key_deletes_in_cascade()
    {
        var cascades = await Sql.QueryAsync<string>(Schema.CascadingForeignKeysQuery);

        Assert.That(cascades, Is.Empty);
    }

    [Test]
    public async Task Tenant_references_are_composite_foreign_keys()
    {
        var foreignKeys = await Sql.QueryAsync<string>(Schema.TwoColumnForeignKeysQuery);

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
        var defaults = await Sql.QueryAsync<string>(Schema.DefaultsQuery);

        Assert.That(defaults, Is.SupersetOf(Schema.Defaults));
    }

    public static IEnumerable<string> ColumnTables() => Columns.Keys;

    /// <summary>
    /// Una columna de <c>information_schema.columns</c>, con el tipo lógico del documento. En SQL Server una columna JSON
    /// es <c>nvarchar(max)</c> con su restricción <c>ISJSON</c> (<see cref="IsJson"/>).
    /// </summary>
    public sealed record ColumnInfo(string Table, string Name, string DataType, int? Length, int? Precision, int? Scale, bool IsNullable)
    {
        public ColumnInfo()
            : this("", "", "", null, null, null, false)
        {
        }

        public bool IsJson { get; init; }

        public string Describe() => $"{Name} {Type()}{(IsNullable ? " null" : "")}";

        private string Type() => DataType switch
        {
            "nvarchar" when Length == -1 => IsJson ? "json" : "text",
            "character varying" or "nvarchar" => $"varchar({Length})",
            "character" or "nchar" => $"char({Length})",
            "numeric" or "decimal" => $"numeric({Precision},{Scale})",
            "integer" => "int",
            "boolean" or "bit" => "bool",
            "timestamp with time zone" or "datetimeoffset" => "ts",
            "time without time zone" => "time",
            "jsonb" => "json",
            "uniqueidentifier" => "uuid",
            _ => DataType,
        };
    }

    /// <summary>Un índice del catálogo, descrito como <c>tabla(columnas) [where filtro]</c>.</summary>
    public sealed record IndexInfo(string Table, string Name, bool IsUnique, bool IsPrimary, string Columns, string? Predicate)
    {
        public IndexInfo()
            : this("", "", false, false, "", null)
        {
        }

        public string Describe() => $"{Table}({Columns}){(Predicate is null ? "" : " where " + Predicate)}";
    }

    /// <summary>Las consultas al catálogo de un motor, que devuelven lo mismo con los nombres y tipos del documento.</summary>
    private abstract class Catalog
    {
        /// <summary>Columnas de las tablas del modelo como <see cref="ColumnInfo"/>; se le agregan condiciones con <c>and</c>.</summary>
        public abstract string ColumnsQuery { get; }

        public abstract string ConstraintAndIndexNamesQuery { get; }

        /// <summary>Restricciones de verificación como <c>tabla.nombre</c>.</summary>
        public abstract string CheckConstraintsQuery { get; }

        /// <summary>Llaves foráneas que borran, anulan o ponen el valor por defecto en cascada.</summary>
        public abstract string CascadingForeignKeysQuery { get; }

        /// <summary>Llaves foráneas de dos columnas como <c>tabla(columnas)->padre</c>.</summary>
        public abstract string TwoColumnForeignKeysQuery { get; }

        /// <summary>Valores por defecto como <c>tabla.columna valor</c>.</summary>
        public abstract string DefaultsQuery { get; }

        /// <summary>Valores por defecto que fija el modelo, como los escribe el motor.</summary>
        public abstract string[] Defaults { get; }

        public abstract Task<List<IndexInfo>> IndexesAsync();
    }

    private sealed class PostgresCatalog : Catalog
    {
        private const string IndexQuery = """
            select t.relname as "Table", i.relname as "Name", ix.indisunique as "IsUnique", ix.indisprimary as "IsPrimary",
                   array_to_string(array(
                       select a.attname || case when ix.indoption[k.ord - 1] & 1 = 1 then ' desc' else '' end
                       from unnest(ix.indkey::int2[]) with ordinality as k(attnum, ord)
                       join pg_attribute a on a.attrelid = t.oid and a.attnum = k.attnum
                       order by k.ord), ',') as "Columns",
                   pg_get_expr(ix.indpred, ix.indrelid) as "Predicate"
            from pg_index ix
            join pg_class i on i.oid = ix.indexrelid
            join pg_class t on t.oid = ix.indrelid
            join pg_namespace n on n.oid = t.relnamespace
            where n.nspname = 'public'
            """;

        public override string ColumnsQuery =>
            "select table_name as \"Table\", column_name as \"Name\", data_type as \"DataType\", character_maximum_length as \"Length\", " +
            "numeric_precision as \"Precision\", numeric_scale as \"Scale\", is_nullable = 'YES' as \"IsNullable\" " +
            "from information_schema.columns where table_schema = 'public'";

        public override string ConstraintAndIndexNamesQuery =>
            "select conname from pg_constraint c join pg_namespace n on n.oid = c.connamespace where n.nspname = 'public' and c.contype <> 'n' " +
            "union all select i.relname from pg_index ix join pg_class i on i.oid = ix.indexrelid join pg_namespace n on " +
            "n.oid = i.relnamespace where n.nspname = 'public' and not exists (select 1 from pg_constraint c where c.conindid = ix.indexrelid)";

        public override string CheckConstraintsQuery =>
            "select t.relname || '.' || c.conname from pg_constraint c join pg_class t on t.oid = c.conrelid " +
            "join pg_namespace n on n.oid = t.relnamespace where n.nspname = 'public' and c.contype = 'c'";

        public override string CascadingForeignKeysQuery =>
            "select conname from pg_constraint where contype = 'f' and confdeltype in ('c', 'n', 'd')";

        public override string TwoColumnForeignKeysQuery => """
            select t.relname || '(' || array_to_string(array(
                       select a.attname from unnest(c.conkey) with ordinality as k(attnum, ord)
                       join pg_attribute a on a.attrelid = c.conrelid and a.attnum = k.attnum order by k.ord), ',')
                   || ')->' || p.relname
            from pg_constraint c join pg_class t on t.oid = c.conrelid join pg_class p on p.oid = c.confrelid
            where c.contype = 'f' and array_length(c.conkey, 1) = 2
            """;

        public override string DefaultsQuery =>
            "select table_name || '.' || column_name || ' ' || column_default from information_schema.columns " +
            "where table_schema = 'public' and column_default is not null";

        public override string[] Defaults =>
        [
            "type_documents.is_active true", "companies.is_active true", "companies.allow_seller_login false",
            "companies.graphic_representation_template 2", "customers.is_active true", "items.is_active true",
            "resellers.is_active true", "documents.prefix ''::character varying", "documents.allowance_total_amount 0.0",
            "documents.charge_total_amount 0.0", "documents.prepaid_amount 0.0", "documents.email_status 0",
            "received_documents.prefix ''::character varying", "document_files.revision 1", "plans.period 0",
            "plan_usage.used 0", "outbox_messages.status 0", "outbox_messages.attempts 0", "notifications.is_read false",
        ];

        public override Task<List<IndexInfo>> IndexesAsync() => Sql.QueryAsync<IndexInfo>(IndexQuery);
    }

    /// <summary>
    /// El catálogo de SQL Server (<c>sys.*</c> e <c>information_schema</c>). Los filtros de los índices se escriben como
    /// en PostgreSQL para compararlos con el documento: <c>([is_active]=(1))</c> queda <c>is_active</c> y
    /// <c>([cufe] IS NOT NULL)</c> queda <c>(cufe IS NOT NULL)</c>.
    /// </summary>
    private sealed partial class SqlServerCatalog : Catalog
    {
        private const string IndexQuery = """
            select t.name as [Table], i.name as [Name], i.is_unique as IsUnique, i.is_primary_key as IsPrimary,
                   string_agg(c.name + case when ic.is_descending_key = 1 then ' desc' else '' end, ',')
                       within group (order by ic.key_ordinal) as [Columns],
                   i.filter_definition as Predicate
            from sys.indexes i
            join sys.tables t on t.object_id = i.object_id
            join sys.index_columns ic on ic.object_id = i.object_id and ic.index_id = i.index_id and ic.is_included_column = 0
            join sys.columns c on c.object_id = ic.object_id and c.column_id = ic.column_id
            where i.type > 0 and schema_name(t.schema_id) = 'dbo'
            group by t.name, i.name, i.is_unique, i.is_primary_key, i.filter_definition
            """;

        public override string ColumnsQuery =>
            "select c.table_name as [Table], c.column_name as [Name], c.data_type as DataType, c.character_maximum_length as Length, " +
            "cast(c.numeric_precision as int) as Precision, c.numeric_scale as Scale, " +
            "cast(case when c.is_nullable = 'YES' then 1 else 0 end as bit) as IsNullable, " +
            "cast(case when exists (select 1 from sys.check_constraints k where k.parent_object_id = object_id(c.table_name) " +
            "and k.name = 'ck_' + c.table_name + '_' + c.column_name + '_json' and k.definition like '%isjson%') then 1 else 0 end as bit) as IsJson " +
            "from information_schema.columns c where c.table_schema = 'dbo'";

        public override string ConstraintAndIndexNamesQuery =>
            "select name from sys.objects where type in ('PK', 'UQ', 'F', 'C') and schema_id = schema_id('dbo') " +
            "union all select i.name from sys.indexes i join sys.tables t on t.object_id = i.object_id " +
            "where i.type > 0 and i.is_primary_key = 0 and i.is_unique_constraint = 0 and schema_name(t.schema_id) = 'dbo'";

        public override string CheckConstraintsQuery =>
            "select t.name + '.' + k.name from sys.check_constraints k join sys.tables t on t.object_id = k.parent_object_id";

        // 0 es NO ACTION: el borrado de la fila padre falla mientras tenga hijas.
        public override string CascadingForeignKeysQuery => "select name from sys.foreign_keys where delete_referential_action <> 0";

        public override string TwoColumnForeignKeysQuery => """
            select t.name + '(' + string_agg(c.name, ',') within group (order by k.constraint_column_id) + ')->' + p.name
            from sys.foreign_keys f
            join sys.foreign_key_columns k on k.constraint_object_id = f.object_id
            join sys.tables t on t.object_id = f.parent_object_id
            join sys.tables p on p.object_id = f.referenced_object_id
            join sys.columns c on c.object_id = k.parent_object_id and c.column_id = k.parent_column_id
            group by f.object_id, t.name, p.name
            having count(*) = 2
            """;

        public override string DefaultsQuery =>
            "select table_name + '.' + column_name + ' ' + column_default from information_schema.columns " +
            "where table_schema = 'dbo' and column_default is not null";

        // Los mismos valores que en PostgreSQL, como los guarda SQL Server.
        public override string[] Defaults =>
        [
            "type_documents.is_active (CONVERT([bit],(1)))", "companies.is_active (CONVERT([bit],(1)))",
            "companies.allow_seller_login (CONVERT([bit],(0)))", "companies.graphic_representation_template ((2))",
            "customers.is_active (CONVERT([bit],(1)))", "items.is_active (CONVERT([bit],(1)))",
            "resellers.is_active (CONVERT([bit],(1)))", "documents.prefix (N'')", "documents.allowance_total_amount ((0.0))",
            "documents.charge_total_amount ((0.0))", "documents.prepaid_amount ((0.0))", "documents.email_status (CONVERT([smallint],(0)))",
            "received_documents.prefix (N'')", "document_files.revision ((1))", "plans.period (CONVERT([smallint],(0)))",
            "plan_usage.used (CONVERT([bigint],(0)))", "outbox_messages.status (CONVERT([smallint],(0)))", "outbox_messages.attempts ((0))",
            "notifications.is_read (CONVERT([bit],(0)))",
        ];

        public override async Task<List<IndexInfo>> IndexesAsync() =>
            [.. (await Sql.QueryAsync<IndexInfo>(IndexQuery)).Select(index => index with { Predicate = Normalize(index.Predicate) })];

        private static string? Normalize(string? predicate) => predicate is null
            ? null
            : IsTrue().Match(predicate) is { Success: true } isTrue
                ? isTrue.Groups["column"].Value
                : predicate.Replace("[", "", StringComparison.Ordinal).Replace("]", "", StringComparison.Ordinal);

        [System.Text.RegularExpressions.GeneratedRegex(@"^\(\[(?<column>\w+)\]=\(1\)\)$")]
        private static partial System.Text.RegularExpressions.Regex IsTrue();
    }
}
