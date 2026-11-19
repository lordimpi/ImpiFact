using System.Data.Common;
using ImpiFact.Domain.Common;
using ImpiFact.Infrastructure.Billing;

namespace ImpiFact.Infrastructure.SqlServer;

/// <summary>
/// Consumo del plan en SQL Server: un <c>MERGE</c> crea la fila de <c>plan_usage</c> en 1 o le suma uno en una sola
/// sentencia. <c>HOLDLOCK</c> (aislamiento serializable solo para esta tabla) bloquea también el rango de la llave
/// cuando la fila todavía no existe: sin él, dos consumos simultáneos podrían no encontrarla y los dos intentarían
/// insertarla. Con él, el segundo espera al primero y luego suma sobre el valor ya actualizado: nunca se pierde una
/// cuenta ni falla por llave repetida. El bloqueo dura hasta el final de la transacción.
/// </summary>
internal sealed class SqlServerPlanUsageSql : IPlanUsageSql
{
    // MERGE exige el punto y coma final.
    internal const string IncrementSql =
        """
        MERGE plan_usage WITH (HOLDLOCK) AS target
        USING (SELECT @company_plan_id AS company_plan_id, @document_kind AS document_kind) AS source
        ON target.company_plan_id = source.company_plan_id AND target.document_kind = source.document_kind
        WHEN MATCHED THEN
            UPDATE SET used = target.used + 1, row_version = target.row_version + 1
        WHEN NOT MATCHED THEN
            INSERT (company_plan_id, document_kind, used, row_version)
            VALUES (source.company_plan_id, source.document_kind, 1, 1);
        """;

    public async Task IncrementAsync(
        DbConnection connection, DbTransaction transaction, Guid companyPlanId, DocumentKind kind, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = IncrementSql;
        SqlCommands.Add(command, "company_plan_id", companyPlanId);
        SqlCommands.Add(command, "document_kind", (short)kind);
        await command.ExecuteNonQueryAsync(ct);
    }
}
