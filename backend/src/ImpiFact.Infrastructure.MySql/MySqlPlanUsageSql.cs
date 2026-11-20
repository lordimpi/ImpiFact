using System.Data.Common;
using ImpiFact.Domain.Common;
using ImpiFact.Infrastructure.Billing;

namespace ImpiFact.Infrastructure.MySql;

/// <summary>
/// Consumo del plan en MySQL: <c>INSERT … ON DUPLICATE KEY UPDATE</c> crea la fila de <c>plan_usage</c> en 1 o le suma
/// uno en una sola sentencia. InnoDB bloquea la fila hasta el final de la transacción, así un segundo consumo
/// simultáneo del mismo plan y familia espera y luego suma sobre el valor ya actualizado: nunca se pierde una cuenta.
/// La única llave única de la tabla es la primaria (<c>company_plan_id, document_kind</c>), así que el «duplicado» solo
/// puede ser esa fila.
/// </summary>
internal sealed class MySqlPlanUsageSql : IPlanUsageSql
{
    internal const string IncrementSql =
        """
        INSERT INTO plan_usage (company_plan_id, document_kind, used, row_version)
        VALUES (@company_plan_id, @document_kind, 1, 1)
        ON DUPLICATE KEY UPDATE used = used + 1, row_version = row_version + 1
        """;

    public async Task IncrementAsync(
        DbConnection connection, DbTransaction transaction, Guid companyPlanId, DocumentKind kind, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = IncrementSql;
        MySqlParameters.Add(command, "company_plan_id", companyPlanId);
        MySqlParameters.Add(command, "document_kind", (short)kind);
        await command.ExecuteNonQueryAsync(ct);
    }
}
