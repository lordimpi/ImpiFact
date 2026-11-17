using System.Data.Common;
using ImpiFact.Domain.Common;
using ImpiFact.Infrastructure.Billing;

namespace ImpiFact.Infrastructure.Postgres;

/// <summary>
/// Consumo del plan en PostgreSQL: <c>INSERT … ON CONFLICT DO UPDATE</c> crea la fila de <c>plan_usage</c> en 1 o le
/// suma uno en una sola sentencia. PostgreSQL bloquea la fila hasta el final de la transacción, así un segundo
/// consumo simultáneo del mismo plan y familia espera y luego suma sobre el valor ya actualizado: nunca se pierde una
/// cuenta. Si dos transacciones crean la fila a la vez, la segunda espera al índice de la llave primaria y suma.
/// </summary>
internal sealed class PostgresPlanUsageSql : IPlanUsageSql
{
    internal const string IncrementSql =
        """
        INSERT INTO plan_usage (company_plan_id, document_kind, used, row_version)
        VALUES (@company_plan_id, @document_kind, 1, 1)
        ON CONFLICT (company_plan_id, document_kind)
        DO UPDATE SET used = plan_usage.used + 1, row_version = plan_usage.row_version + 1
        """;

    public async Task IncrementAsync(
        DbConnection connection, DbTransaction transaction, Guid companyPlanId, DocumentKind kind, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = IncrementSql;
        Add(command, "company_plan_id", companyPlanId);
        Add(command, "document_kind", (short)kind);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
