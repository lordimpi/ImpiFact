using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;

namespace ImpiFact.Architecture.Tests;

/// <summary>
/// Reglas de frontera de Arquitectura detallada, secciones 1 y 16, además de las dependencias entre capas: el
/// controlador de cada motor (PostgreSQL, SQL Server) solo vive en su proyecto de proveedor, los SDK de las nubes solo en
/// los adaptadores de almacenamiento y secretos, los endpoints no acceden a datos y toda consulta
/// Dapper (la librería de lecturas con SQL escrito a mano) filtra por empresa.
/// </summary>
public class BoundaryTests
{
    /// <summary>Tablas sin <c>company_id</c> que una consulta puede leer sin filtrar por empresa.</summary>
    private static readonly HashSet<string> TablesWithoutCompany = new(StringComparer.OrdinalIgnoreCase)
    {
        "countries", "credit_note_discrepancy_responses", "credit_note_discrepancy_responses_sd", "debit_note_discrepancy_responses",
        "departments", "discounts", "events", "incoterms", "languages", "municipalities", "payment_forms", "payment_methods",
        "prepaid_payment_types", "reference_prices", "taxes", "type_currencies", "type_discounts", "type_document_identifications",
        "type_documents", "type_environments", "type_generation_transmitions", "type_item_identifications", "type_liabilities",
        "type_operations", "type_organizations", "type_regimes", "type_rejections", "type_spds", "unit_measures",
        "roles", "plans", "plan_quotas", "resellers",
    };

    private static readonly Regex StringLiteral = new("\"\"\"(?<raw>[\\s\\S]*?)\"\"\"|@\"(?<verbatim>(?:[^\"]|\"\")*)\"|\"(?<plain>(?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

    private static readonly Regex FromOrJoin = new(@"\b(?:from|join)\s+(?<table>[a-z_][a-z0-9_]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Test]
    public void Only_the_postgres_provider_uses_the_postgres_driver()
    {
        var result = Types.InAssemblies([Load("ImpiFact.Infrastructure"), Load("ImpiFact.Application"), Load("ImpiFact.Api"), Load("ImpiFact.Worker")])
            .ShouldNot()
            .HaveDependencyOnAny("Npgsql")
            .GetResult();

        Assert.That(result.FailingTypeNames ?? [], Is.Empty, "Npgsql belongs in ImpiFact.Infrastructure.Postgres.");
    }

    [Test]
    public void Only_the_sql_server_provider_uses_the_sql_server_driver()
    {
        var result = Types.InAssemblies([Load("ImpiFact.Infrastructure"), Load("ImpiFact.Application"), Load("ImpiFact.Api"), Load("ImpiFact.Worker")])
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.Data.SqlClient", "Microsoft.EntityFrameworkCore.SqlServer")
            .GetResult();

        Assert.That(result.FailingTypeNames ?? [], Is.Empty, "SqlClient belongs in ImpiFact.Infrastructure.SqlServer.");
    }

    // MySqlConnector (el controlador de MySQL) y el proveedor de EF de Microting solo viven en su proyecto de proveedor,
    // y los proveedores de PostgreSQL y SQL Server tampoco los usan.
    [Test]
    public void Only_the_mysql_provider_uses_the_mysql_driver()
    {
        var result = Types.InAssemblies([
                Load("ImpiFact.Infrastructure"), Load("ImpiFact.Application"), Load("ImpiFact.Api"), Load("ImpiFact.Worker"),
                Load("ImpiFact.Infrastructure.Postgres"), Load("ImpiFact.Infrastructure.SqlServer"),
            ])
            .ShouldNot()
            .HaveDependencyOnAny("MySqlConnector", "Microting")
            .GetResult();

        Assert.That(result.FailingTypeNames ?? [], Is.Empty, "MySqlConnector belongs in ImpiFact.Infrastructure.MySql.");
    }

    /// <summary>Espacios de nombres de los SDK de las nubes (Azure, AWS, Google Cloud) y del gRPC que usa el de Google.</summary>
    private static readonly string[] CloudSdkNamespaces = ["Azure", "Amazon", "Google.Cloud", "Google.Apis", "Google.Api.Gax", "Grpc"];

    // Los SDK de las nubes solo los usan los adaptadores de almacenamiento y de secretos (y sus pruebas de salud, en
    // Observability): el resto de Infrastructure, los proveedores de base y los hosts nunca los nombran.
    [Test]
    public void Only_the_storage_secrets_and_readiness_adapters_use_the_cloud_sdks()
    {
        var infrastructure = Types.InAssembly(Load("ImpiFact.Infrastructure"))
            .That()
            .DoNotResideInNamespace("ImpiFact.Infrastructure.Storage")
            .And()
            .DoNotResideInNamespace("ImpiFact.Infrastructure.Protection")
            .And()
            .DoNotResideInNamespace("ImpiFact.Infrastructure.Observability")
            .ShouldNot()
            .HaveDependencyOnAny(CloudSdkNamespaces)
            .GetResult();
        var others = Types.InAssemblies([
                Load("ImpiFact.Application"), Load("ImpiFact.Api"), Load("ImpiFact.Worker"),
                Load("ImpiFact.Infrastructure.Postgres"), Load("ImpiFact.Infrastructure.SqlServer"), Load("ImpiFact.Infrastructure.MySql"),
            ])
            .ShouldNot()
            .HaveDependencyOnAny(CloudSdkNamespaces)
            .GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(infrastructure.FailingTypeNames ?? [], Is.Empty, "Cloud SDKs belong in Infrastructure/Storage and Infrastructure/Protection.");
            Assert.That(others.FailingTypeNames ?? [], Is.Empty, "Only Infrastructure talks to the cloud SDKs.");
        });
    }

    // Los endpoints solo traducen HTTP a un caso de uso: no abren el DbContext ni escriben SQL.
    [Test]
    public void Api_endpoints_do_not_reach_the_database()
    {
        var result = Types.InAssembly(Load("ImpiFact.Api"))
            .That()
            .DoNotHaveName("ApiComposition", "Program")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore", "Dapper", "Npgsql", "Microsoft.Data.SqlClient", "ImpiFact.Infrastructure.Persistence",
                "MySqlConnector")
            .GetResult();

        Assert.That(result.FailingTypeNames ?? [], Is.Empty);
    }

    [Test]
    public void Every_dapper_query_on_a_company_table_filters_by_company_id()
    {
        var offenders = DapperSqlLiterals()
            .Where(literal => ReadsCompanyTable(literal.Sql) && !literal.Sql.Contains("company_id", StringComparison.OrdinalIgnoreCase))
            .Select(literal => $"{literal.File}: {literal.Sql.Trim()}");

        Assert.That(offenders, Is.Empty, "Toda consulta Dapper incluye company_id en el WHERE (Arquitectura detallada, sección 16).");
    }

    [TestCase("select id, name from customers where company_id = @CompanyId", false)]
    [TestCase("select d.id from documents d join customers c on c.id = d.customer_id", true)]
    [TestCase("select code, name from municipalities where department_id = @DepartmentId", false)]
    public void The_company_id_rule_recognises_company_tables(string sql, bool offends)
    {
        Assert.That(ReadsCompanyTable(sql) && !sql.Contains("company_id", StringComparison.OrdinalIgnoreCase), Is.EqualTo(offends));
    }

    private static bool ReadsCompanyTable(string sql) =>
        FromOrJoin.Matches(sql).Any(match => !TablesWithoutCompany.Contains(match.Groups["table"].Value));

    // Revisa el código fuente, no el compilado: las consultas son cadenas de texto dentro de los archivos que usan Dapper.
    private static IEnumerable<(string File, string Sql)> DapperSqlLiterals()
    {
        var src = Path.Combine(SourceProjects.BackendRoot(), "src");
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            if (!text.Contains("using Dapper;", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in StringLiteral.Matches(text))
            {
                var sql = match.Groups["raw"].Success ? match.Groups["raw"].Value
                    : match.Groups["verbatim"].Success ? match.Groups["verbatim"].Value
                    : match.Groups["plain"].Value;
                if (Regex.IsMatch(sql, @"\bselect\b", RegexOptions.IgnoreCase))
                {
                    yield return (Path.GetRelativePath(src, file), sql);
                }
            }
        }
    }

    private static Assembly Load(string name) => Assembly.Load(new AssemblyName(name));
}
