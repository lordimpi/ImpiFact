using System.Data.Common;
using ImpiFact.Application.Abstractions.Persistence;
using MySqlConnector;

namespace ImpiFact.Infrastructure.MySql;

/// <summary>Conexiones de lectura para Dapper tomadas del origen de datos de MySqlConnector (el controlador .NET de MySQL).</summary>
internal sealed class MySqlReadConnectionFactory(MySqlDataSource dataSource) : IReadConnectionFactory
{
    public async Task<DbConnection> OpenAsync(CancellationToken ct) => await dataSource.OpenConnectionAsync(ct);
}
