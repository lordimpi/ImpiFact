using System.Data.Common;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Infrastructure.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.SqlServer;

/// <summary>
/// Conexiones de lectura para Dapper con SqlClient (el controlador .NET de SQL Server). Con la misma cadena que EF
/// Core, SqlClient las saca del mismo grupo de conexiones abiertas.
/// </summary>
internal sealed class SqlServerReadConnectionFactory(IOptions<DatabaseOptions> options) : IReadConnectionFactory
{
    public async Task<DbConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqlConnection(options.Value.ConnectionString);
        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
