using System.Globalization;
using ImpiFact.Infrastructure.Persistence;

namespace ImpiFact.Infrastructure.MySql;

/// <summary>Trozos de SQL de MySQL 8 para las lecturas con Dapper de Infrastructure (ver <see cref="ISqlDialect"/>).</summary>
internal sealed class MySqlSqlDialect : ISqlDialect
{
    /// <summary>
    /// Intercalación (collation: la regla para comparar y ordenar texto) de las búsquedas sin mayúsculas: la misma de
    /// las columnas, pero <c>_ci</c> (sin distinguir mayúsculas) y <c>_as</c> (distinguiendo tildes), como <c>ilike</c>.
    /// </summary>
    internal const string CaseInsensitiveCollation = "utf8mb4_0900_as_ci";

    // MySQL acepta TRUE y FALSE (son 1 y 0 en las columnas tinyint(1)).
    public string Bool(bool value) => value ? "true" : "false";

    // MySqlConnector reemplaza los parámetros en el cliente, así que «limit @Take» llega como un número.
    public string Paging(string take, string? skip = null) =>
        skip is null ? $"limit {take}" : $"limit {take} offset {skip}";

    // Las columnas usan utf8mb4_0900_as_cs (distingue mayúsculas, como PostgreSQL) y el texto de un JSON llega como
    // utf8mb4_bin: el «collate» hace que las dos comparen sin mayúsculas. «'\\'» es una sola barra invertida para MySQL
    // (con el modo NO_BACKSLASH_ESCAPES apagado, el valor por defecto).
    public string LikeIgnoreCase(string expression, string pattern) =>
        $"{expression} collate {CaseInsensitiveCollation} like {pattern} escape '\\\\'";

    // «->>» es el atajo de json_unquote(json_extract(...)): el texto sin comillas, o nulo si la propiedad no está.
    public string JsonText(string column, string property) => $"{column} ->> '$.{property}'";

    // Las columnas datetime(6) ya guardan la hora UTC sin zona: basta sumar el desfase.
    public string YearMonthAtOffset(string timestamp, int utcOffsetHours) =>
        $"date_format(date_add({timestamp}, interval {utcOffsetHours.ToString(CultureInfo.InvariantCulture)} hour), '%Y-%m')";

    // Comillas invertidas: así un nombre que MySQL reserva (como key o groups) sirve de columna.
    public string Identifier(string name) => $"`{name}`";

    // LATERAL existe desde MySQL 8.0.14.
    public string OuterApply(string subquery, string alias) => $"left join lateral ({subquery}) {alias} on true";
}
