using System.Data.Common;
using ImpiFact.Application.Abstractions.Persistence;
using Npgsql;

namespace ImpiFact.Infrastructure.Postgres;

/// <summary>Conexiones de lectura para Dapper tomadas del origen de datos de Npgsql (el controlador .NET de PostgreSQL).</summary>
internal sealed class NpgsqlReadConnectionFactory(NpgsqlDataSource dataSource) : IReadConnectionFactory
{
    public async Task<DbConnection> OpenAsync(CancellationToken ct) => await dataSource.OpenConnectionAsync(ct);
}
