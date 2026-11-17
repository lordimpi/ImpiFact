using System.Data.Common;
using ImpiFact.Domain.Common;
using ImpiFact.Infrastructure.Persistence;

namespace ImpiFact.Infrastructure.Billing;

/// <summary>
/// Consumos del plan que esperan el próximo guardado de la solicitud o del mensaje (uno por documento aceptado). La
/// unidad de trabajo los ejecuta con <see cref="IPlanUsageSql"/> en la transacción del guardado, después de los
/// cambios de EF Core: si el documento no se pudo marcar aceptado (por ejemplo, porque otro proceso lo aceptó antes y
/// su versión de fila cambió), el consumo no se escribe.
/// </summary>
internal sealed class PendingPlanUsage(IPlanUsageSql sql) : IPendingWrites
{
    private readonly List<(Guid CompanyPlanId, DocumentKind Kind)> pending = [];

    public bool HasPending => pending.Count > 0;

    public void Add(Guid companyPlanId, DocumentKind kind) => pending.Add((companyPlanId, kind));

    public async Task FlushAsync(DbConnection connection, DbTransaction transaction, CancellationToken ct)
    {
        foreach (var (companyPlanId, kind) in pending)
        {
            await sql.IncrementAsync(connection, transaction, companyPlanId, kind, ct);
        }

        pending.Clear();
    }

    public void Discard() => pending.Clear();
}
