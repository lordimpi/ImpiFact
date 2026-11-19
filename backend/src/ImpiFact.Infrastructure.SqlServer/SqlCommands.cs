using System.Data.Common;

namespace ImpiFact.Infrastructure.SqlServer;

/// <summary>Ayudas para armar los comandos del SQL propio de SQL Server.</summary>
internal static class SqlCommands
{
    /// <summary>Agrega un parámetro con nombre (sin la arroba); SqlClient deduce el tipo del valor.</summary>
    public static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@" + name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
