using System.Globalization;
using ImpiFact.Infrastructure.Persistence;

namespace ImpiFact.Infrastructure.SqlServer;

/// <summary>Trozos de SQL de SQL Server para las lecturas con Dapper de Infrastructure (ver <see cref="ISqlDialect"/>).</summary>
internal sealed class SqlServerSqlDialect : ISqlDialect
{
    // SQL Server no tiene literales booleanos: una columna bit se compara con 1 o 0.
    public string Bool(bool value) => value ? "1" : "0";

    // offset … fetch exige un order by antes (la consulta siempre lo trae) y un offset aunque no se salte ninguna fila.
    public string Paging(string take, string? skip = null) => $"offset {skip ?? "0"} rows fetch next {take} rows only";

    // Las columnas de texto distinguen mayúsculas (SqlServerModelCustomizer.TextCollation): la búsqueda pide una
    // intercalación que no las distingue, igual que ilike. En T-SQL '\' es un carácter común dentro de las comillas.
    public string LikeIgnoreCase(string expression, string pattern) =>
        $"({expression}) collate {SqlServerModelCustomizer.IgnoreCaseCollation} like {pattern} escape '\\'";

    public string JsonText(string column, string property) => $"json_value({column}, '$.{property}')";

    // switchoffset pasa el instante (datetimeoffset) a la hora del desfase y el estilo 126 lo escribe como
    // «yyyy-mm-ddThh:mi:ss…»: los 7 primeros caracteres son el año y el mes. Es determinista, a diferencia de format.
    public string YearMonthAtOffset(string timestamp, int utcOffsetHours)
    {
        var sign = utcOffsetHours < 0 ? "-" : "+";
        var hours = Math.Abs(utcOffsetHours).ToString("00", CultureInfo.InvariantCulture);
        return $"convert(char(7), switchoffset({timestamp}, '{sign}{hours}:00'), 126)";
    }

    public string OuterApply(string subquery, string alias) => $"outer apply ({subquery}) {alias}";
}
