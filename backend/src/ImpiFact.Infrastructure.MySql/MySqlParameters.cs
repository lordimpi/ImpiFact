using System.Data.Common;

namespace ImpiFact.Infrastructure.MySql;

/// <summary>Parámetros de los comandos escritos a mano de este proyecto.</summary>
internal static class MySqlParameters
{
    /// <summary>
    /// Agrega <c>@name</c>. Un <see cref="DateTimeOffset"/> va como la hora UTC sin zona, que es lo que guardan las
    /// columnas <c>datetime(6)</c> (MySQL no tiene un tipo con desfase).
    /// </summary>
    public static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value is DateTimeOffset instant ? instant.UtcDateTime : value;
        command.Parameters.Add(parameter);
    }
}
