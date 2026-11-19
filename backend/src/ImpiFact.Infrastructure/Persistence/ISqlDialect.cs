namespace ImpiFact.Infrastructure.Persistence;

/// <summary>
/// Trozos de SQL que cambian de un motor a otro, para las lecturas con Dapper (la librería que ejecuta SQL escrito a
/// mano) que viven en Infrastructure. Cada proyecto <c>ImpiFact.Infrastructure.&lt;Motor&gt;</c> trae su implementación
/// y la registra con su <c>IDatabaseProvider</c>, como el SQL del consecutivo. El resto de cada consulta es SQL común a
/// PostgreSQL, SQL Server y MySQL, y una prueba de arquitectura revisa que no se cuele sintaxis de un solo motor.
/// </summary>
/// <remarks>
/// Cada método recibe expresiones o nombres de parámetro ya armados por quien llama (nunca texto del cliente) y devuelve
/// el trozo sin espacios alrededor. Las expresiones van tal cual: el dialecto no las escapa.
/// </remarks>
public interface ISqlDialect
{
    /// <summary>
    /// Literal booleano para comparar una columna, por ejemplo <c>c.is_active = {Bool(true)}</c>: <c>true</c> en
    /// PostgreSQL y MySQL, <c>1</c> en SQL Server (que no tiene literales booleanos).
    /// </summary>
    string Bool(bool value);

    /// <summary>
    /// Límite de filas al final de la consulta, después del <c>order by</c> (obligatorio en SQL Server):
    /// <c>limit @Take offset @Skip</c> en PostgreSQL y MySQL, <c>offset @Skip rows fetch next @Take rows only</c> en
    /// SQL Server.
    /// </summary>
    /// <param name="take">Parámetro o literal con la cantidad de filas, por ejemplo <c>@Take</c> o <c>1</c>.</param>
    /// <param name="skip">Parámetro con las filas que se saltan, o nulo para no saltar ninguna.</param>
    string Paging(string take, string? skip = null);

    /// <summary>
    /// Condición «el texto contiene el patrón, sin distinguir mayúsculas»: <c>expression ilike pattern escape '\'</c>
    /// en PostgreSQL. El patrón es de LIKE y llega con sus comodines escapados con la barra invertida
    /// (<c>\%</c>, <c>\_</c>, <c>\\</c>), así que el dialecto debe usar <c>\</c> como carácter de escape.
    /// </summary>
    string LikeIgnoreCase(string expression, string pattern);

    /// <summary>
    /// Texto de una propiedad de primer nivel de una columna JSON (nulo si no está): <c>column ->> 'property'</c> en
    /// PostgreSQL, <c>json_value(column, '$.property')</c> en SQL Server.
    /// </summary>
    /// <param name="property">Nombre de la propiedad, fijo en el código (letras, dígitos y guion bajo).</param>
    string JsonText(string column, string property);

    /// <summary>
    /// Año y mes (<c>YYYY-MM</c>) de un instante visto con un desfase fijo respecto a UTC, para agrupar por mes en hora
    /// Colombia (UTC-5 todo el año, sin horario de verano).
    /// </summary>
    /// <param name="timestamp">Expresión con un instante guardado en UTC (<c>timestamptz</c>, <c>datetimeoffset</c> o <c>datetime</c> en UTC).</param>
    /// <param name="utcOffsetHours">Horas que se suman a UTC; -5 para Colombia.</param>
    string YearMonthAtOffset(string timestamp, int utcOffsetHours);

    /// <summary>
    /// Une a cada fila el resultado de una subconsulta correlacionada (que puede usar columnas de las tablas
    /// anteriores) y deja la fila aunque la subconsulta no devuelva nada: <c>left join lateral (…) alias on true</c> en
    /// PostgreSQL y MySQL, <c>outer apply (…) alias</c> en SQL Server.
    /// </summary>
    string OuterApply(string subquery, string alias);

    /// <summary>
    /// Una columna de instante en la lista del <c>select</c>, para leerla como <see cref="DateTime"/> en UTC en las filas
    /// de Dapper: Npgsql (PostgreSQL) y MySQL ya entregan <c>DateTime</c>, así que va tal cual; SqlClient entrega
    /// <c>datetimeoffset</c> como <see cref="DateTimeOffset"/>, que Dapper no convierte, y SQL Server la pasa a
    /// <c>datetime2</c> (la hora UTC guardada, sin el desfase, que siempre es cero).
    /// </summary>
    /// <param name="column">Columna con un instante guardado en UTC, por ejemplo <c>d.issued_at</c>.</param>
    string Instant(string column) => column;

    /// <summary>
    /// Un nombre de columna que es palabra reservada en algún motor, como <c>percent</c> en SQL Server: va tal cual en
    /// PostgreSQL y MySQL, y entre corchetes en SQL Server. El alias de la columna se elige sin palabras reservadas.
    /// </summary>
    /// <param name="name">Nombre de la columna, fijo en el código (letras minúsculas y guion bajo).</param>
    string Identifier(string name) => name;
}
