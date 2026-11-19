using System.Text.RegularExpressions;

namespace ImpiFact.Architecture.Tests;

/// <summary>
/// Portabilidad entre motores (Arquitectura detallada, sección 16; Modelo de datos detallado, sección 8): el SQL que vive
/// en <c>ImpiFact.Infrastructure</c> sirve igual en PostgreSQL, SQL Server y MySQL. Lo que cambia de un motor a otro lo
/// aporta el dialecto (<c>ISqlDialect</c>) o el personalizador del modelo de cada proyecto
/// <c>ImpiFact.Infrastructure.&lt;Motor&gt;</c>.
/// </summary>
public class PortableSqlTests
{
    /// <summary>Sintaxis de un solo motor (o de dos) que no puede aparecer en el SQL común, con la razón.</summary>
    private static readonly (Regex Pattern, string Reason)[] EngineSpecific =
    [
        (Token(@"\bilike\b"), "ilike is PostgreSQL only; use ISqlDialect.LikeIgnoreCase"),
        (Token("::"), ":: casts are PostgreSQL only"),
        (Token("->"), "JSON operators differ per engine; use ISqlDialect.JsonText"),
        (Token(@"\blimit\b"), "limit is not SQL Server; use ISqlDialect.Paging"),
        (Token(@"\boffset\b|\bfetch\s+next\b|\btop\s*\(?\s*[@\d]"), "row limits differ per engine; use ISqlDialect.Paging"),
        (Token(@"\bto_char\b|\bdate_trunc\b|\bdate_format\b|\bat\s+time\s+zone\b|\binterval\b|\bnow\s*\("), "date functions differ per engine; use ISqlDialect.YearMonthAtOffset or a parameter"),
        (Token(@"\blateral\b|\bouter\s+apply\b|\bcross\s+apply\b"), "correlated joins differ per engine; use ISqlDialect.OuterApply"),
        (Token(@"\|\|"), "|| is not concatenation in SQL Server nor in MySQL"),
        (Token(@"\b(?:true|false)\b"), "SQL Server has no boolean literals; use ISqlDialect.Bool"),
        (Token(@"\b(?:where|and|or|not|on)\s+[a-z_]+\.is_[a-z_]+\b(?!\s*(?:=|<>|!=|\bis\b))"), "a bare boolean column is not a condition in SQL Server; compare it with ISqlDialect.Bool"),
        (Token(@"\bgroup\s+by\s+\d"), "SQL Server does not group by position; repeat the expression"),
        (Token(@"\breturning\b|\bon\s+conflict\b|\bskip\s+locked\b|\bon\s+duplicate\s+key\b"), "engine-specific writes belong in ImpiFact.Infrastructure.<Engine>"),
        (Token(@"(?<![""@])\b(?:percent|key|file|plan|rule|user)\b(?!\s*\(|"")"), "a SQL Server reserved word as a column or alias; use ISqlDialect.Identifier and another alias"),
    ];

    /// <summary>Huecos de una cadena interpolada sin comillas adentro, como <c>{dialect.Bool(true)}</c>: son código C#, no SQL.</summary>
    private static readonly Regex InterpolationHole = new(@"\{[^{}""]*\}", RegexOptions.Compiled);

    private static readonly Regex StringLiteral = new("\"\"\"(?<raw>[\\s\\S]*?)\"\"\"|@\"(?<verbatim>(?:[^\"]|\"\")*)\"|\"(?<plain>(?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

    [Test]
    public void Shared_dapper_sql_has_no_engine_specific_syntax()
    {
        var offenders = SharedDapperLiterals()
            .SelectMany(literal => EngineSpecific
                .Where(rule => rule.Pattern.IsMatch(literal.Text))
                .Select(rule => $"{literal.File}: {rule.Reason}: {literal.Text.Trim()}"));

        Assert.That(offenders, Is.Empty, "The SQL in ImpiFact.Infrastructure must run on PostgreSQL, SQL Server and MySQL.");
    }

    [TestCase("select c.id from customers c where c.name ilike @Pattern", true)]
    [TestCase("'R:' || r.id::text", true)]
    [TestCase(" order by d.issued_at desc, d.id desc limit @Take", true)]
    [TestCase("where c.company_id = @CompanyId and c.is_active", true)]
    [TestCase(" and not i.is_active", true)]
    [TestCase("select to_char(d.issued_at, 'YYYY-MM') from documents d group by 1", true)]
    [TestCase("left join lateral (select 1) p on true", true)]
    [TestCase("where c.company_id = @CompanyId and c.is_active = {dialect.Bool(true)}", false)]
    [TestCase(" and n.is_read = @IsRead", false)]
    [TestCase("select t.percent as Percent from document_taxes t", true)]
    [TestCase("select k.key from idempotency_keys k", true)]
    [TestCase("select t.{dialect.Identifier(\"percent\")} as TaxPercent from document_taxes t", false)]
    [TestCase("select d.id from documents d where d.company_id = @CompanyId and d.status in (@Status0) order by d.issued_at", false)]
    public void The_rules_recognise_engine_specific_sql(string sql, bool offends)
    {
        var text = InterpolationHole.Replace(sql, " ");
        Assert.That(EngineSpecific.Any(rule => rule.Pattern.IsMatch(text)), Is.EqualTo(offends));
    }

    // Los filtros de los índices parciales se escriben distinto en cada motor (MySQL no los tiene): las configuraciones
    // comunes declaran la intención con HasPortableFilter y el personalizador del modelo de cada motor escribe el filtro.
    [Test]
    public void Shared_model_configuration_declares_no_engine_specific_index_filter_or_column_type()
    {
        var offenders = SharedSourceFiles()
            .Where(file => Regex.IsMatch(File.ReadAllText(file.Path), @"\.(?:HasFilter|SetFilter|HasColumnType|SetColumnType)\s*\("))
            .Select(file => file.Relative);

        Assert.That(offenders, Is.Empty, "Index filters and column types belong in the model customizer of ImpiFact.Infrastructure.<Engine>.");
    }

    // Los hosts registran todos los motores y Database:Provider elige uno: un motor nuevo se agrega a los tres.
    [TestCase("ImpiFact.Api")]
    [TestCase("ImpiFact.Worker")]
    [TestCase("ImpiFact.Migrator")]
    public void Every_host_references_every_database_provider(string host)
    {
        var projects = SourceProjects.All();
        var engines = projects.Select(project => project.Name).Where(name => name.StartsWith("ImpiFact.Infrastructure.", StringComparison.Ordinal)).ToList();

        Assert.That(engines, Is.Not.Empty);
        Assert.That(projects.Single(project => project.Name == host).ProjectReferences, Is.SupersetOf(engines));
    }

    private static Regex Token(string pattern) => new(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Revisa el código fuente: las consultas y sus trozos son cadenas de texto dentro de los archivos que usan Dapper.
    private static IEnumerable<(string File, string Text)> SharedDapperLiterals()
    {
        foreach (var file in SharedSourceFiles())
        {
            var text = File.ReadAllText(file.Path);
            if (!text.Contains("using Dapper;", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in StringLiteral.Matches(text))
            {
                var literal = match.Groups["raw"].Success ? match.Groups["raw"].Value
                    : match.Groups["verbatim"].Success ? match.Groups["verbatim"].Value
                    : match.Groups["plain"].Value;
                yield return (file.Relative, InterpolationHole.Replace(literal, " "));
            }
        }
    }

    private static IEnumerable<(string Path, string Relative)> SharedSourceFiles()
    {
        var project = Path.Combine(SourceProjects.BackendRoot(), "src", "ImpiFact.Infrastructure");
        return Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Select(path => (path, Path.GetRelativePath(project, path)));
    }
}
