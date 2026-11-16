using Dapper;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Integration.Tests.Model;

/// <summary>Consultas directas al esquema físico con Dapper, fuera de los filtros de EF.</summary>
internal static class Sql
{
    public static async Task<List<T>> QueryAsync<T>(string sql, object? parameters = null)
    {
        await using var session = ModelDatabase.Open(companyId: null);
        var connection = session.Db.Database.GetDbConnection();
        await connection.OpenAsync();
        return [.. await connection.QueryAsync<T>(sql, parameters)];
    }

    public static async Task<List<IDictionary<string, object?>>> RowsAsync(string sql)
    {
        await using var session = ModelDatabase.Open(companyId: null);
        var connection = session.Db.Database.GetDbConnection();
        await connection.OpenAsync();
        var rows = await connection.QueryAsync(sql);
        return [.. rows.Select(row => (IDictionary<string, object?>)row)];
    }
}
