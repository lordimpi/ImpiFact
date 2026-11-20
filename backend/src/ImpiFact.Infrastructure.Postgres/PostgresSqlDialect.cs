using System.Globalization;
using ImpiFact.Infrastructure.Persistence;

namespace ImpiFact.Infrastructure.Postgres;

/// <summary>Trozos de SQL de PostgreSQL para las lecturas con Dapper de Infrastructure (ver <see cref="ISqlDialect"/>).</summary>
internal sealed class PostgresSqlDialect : ISqlDialect
{
    public string Bool(bool value) => value ? "true" : "false";

    public string Paging(string take, string? skip = null) =>
        skip is null ? $"limit {take}" : $"limit {take} offset {skip}";

    // Con standard_conforming_strings activo (el valor por defecto desde PostgreSQL 9.1) '\' es una barra invertida.
    public string LikeIgnoreCase(string expression, string pattern) => $"{expression} ilike {pattern} escape '\\'";

    public string JsonText(string column, string property) => $"{column} ->> '{property}'";

    // issued_at es timestamptz: «at time zone 'UTC'» lo pasa a la hora UTC sin zona y luego se suma el desfase.
    public string YearMonthAtOffset(string timestamp, int utcOffsetHours)
    {
        var sign = utcOffsetHours < 0 ? "-" : "+";
        var hours = Math.Abs(utcOffsetHours).ToString(CultureInfo.InvariantCulture);
        return $"to_char(({timestamp} at time zone 'UTC') {sign} interval '{hours} hours', 'YYYY-MM')";
    }

    public string OuterApply(string subquery, string alias) => $"left join lateral ({subquery}) {alias} on true";
}
