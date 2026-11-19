using Dapper;
using ImpiFact.Application.Abstractions.Billing;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Application.Features.Dashboard;
using ImpiFact.Application.Features.Documents.Queries;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Documents;
using ImpiFact.Infrastructure.Persistence;

namespace ImpiFact.Infrastructure.Dashboard;

/// <summary>
/// Lecturas del tablero del panel. Los conteos, el certificado y las resoluciones van con Dapper (una librería que
/// ejecuta SQL escrito a mano) y llevan <c>company_id</c> en el WHERE, porque los filtros globales de EF Core (Entity
/// Framework) no aplican ahí. El plan se lee con el mismo puerto y las mismas reglas del módulo Billing
/// (<see cref="IPlanStore.FindCurrentAsync"/> y <see cref="PlanRules.PeriodEnd"/>), así el tablero muestra lo mismo que
/// revisa la emisión.
/// </summary>
/// <remarks>
/// Costo: cuatro consultas por llamada, sin importar el volumen. El conteo usa el índice
/// <c>ix_documents_company_issued</c> (company_id, issued_at DESC) con el rango de los 12 meses y agrupa en la base, así
/// que devuelve a lo sumo 12 meses × tipos × estados filas. El SQL es común a los tres motores; el mes en hora Colombia,
/// el límite y los booleanos los pone el dialecto del motor (<see cref="ISqlDialect"/>).
/// </remarks>
internal sealed class DashboardReads(IReadConnectionFactory connections, IPlanStore plans, ISqlDialect dialect) : IDashboardReads
{
    /// <summary>Colombia está en UTC-5 todo el año (no tiene horario de verano).</summary>
    private const int ColombiaUtcOffsetHours = -5;

    // Los consecutivos de las resoluciones tienen la llave «R:{id}» (NumberingScope.ForResolution). Se unen a cada
    // resolución aquí y no en el SQL: cada motor escribe un uuid como texto a su manera (SQL Server, en mayúsculas).
    private const string ResolutionSequencesSql = """
        select ns.scope_key as ScopeKey, ns.next_number as NextNumber
        from numbering_sequences ns
        where ns.company_id = @CompanyId and ns.scope_key like 'R:%'
        """;

    public async Task<IReadOnlyList<DocumentCountRow>> CountDocumentsAsync(Guid companyId, DateTimeOffset from, DateTimeOffset until, CancellationToken ct)
    {
        // El mes en hora Colombia. Se agrupa por la misma expresión del select: SQL Server no agrupa por posición.
        var month = dialect.YearMonthAtOffset("d.issued_at", ColombiaUtcOffsetHours);
        var sql = $"""
            select {month} as Month, d.document_type_id as DocumentTypeId, d.status as Status, count(*) as Total
            from documents d
            where d.company_id = @CompanyId and d.issued_at >= @From and d.issued_at < @Until
            group by {month}, d.document_type_id, d.status
            """;
        await using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<CountRow>(new CommandDefinition(
            sql, new { CompanyId = companyId, From = from.ToUniversalTime(), Until = until.ToUniversalTime() }, cancellationToken: ct));
        return rows
            .Select(row => new DocumentCountRow(
                row.Month, row.DocumentTypeId, DocumentKinds.NameOf(row.DocumentTypeId), ((DocumentStatus)row.Status).ToString(), row.Total))
            .ToList();
    }

    public async Task<DashboardPlan?> FindCurrentPlanAsync(Guid companyId, CancellationToken ct)
    {
        if (await plans.FindCurrentAsync(companyId, ct) is not { } current)
        {
            return null;
        }

        var plan = current.Plan.Plan;
        var unlimited = plan.Period == PlanPeriod.Default;
        var quotas = current.Plan.Quotas.Where(quota => quota.Value > 0).Select(quota => quota.Key)
            .Union(current.Used.Where(used => used.Value > 0).Select(used => used.Key))
            .Order()
            .Select(kind => new DashboardQuota(kind, current.UsedOf(kind), unlimited ? null : current.QuantityOf(kind)))
            .ToList();
        return new DashboardPlan(
            plan.Name, PeriodName(plan.Period), plan.IsActive, current.StartsAt, PlanRules.PeriodEnd(plan.Period, current.StartsAt, current.EndsAt), quotas);
    }

    public async Task<DashboardCertificate?> FindActiveCertificateAsync(Guid companyId, CancellationToken ct)
    {
        var sql = $"""
            select c.name as Name, c.not_after as NotAfter
            from certificates c
            where c.company_id = @CompanyId and c.is_active = {dialect.Bool(true)}
            order by c.not_after desc
            {dialect.Paging("1")}
            """;
        await using var connection = await connections.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<CertificateRow>(
            new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: ct));
        return row is null ? null : new DashboardCertificate(row.Name, new DateTimeOffset(DateTime.SpecifyKind(row.NotAfter, DateTimeKind.Utc)));
    }

    public async Task<IReadOnlyList<DashboardResolution>> ListActiveResolutionsAsync(Guid companyId, CancellationToken ct)
    {
        var sql = $"""
            select r.id as Id, r.document_type_id as DocumentTypeId, r.prefix as Prefix, r.resolution_number as ResolutionNumber,
                r.range_from as RangeFrom, r.range_to as RangeTo, r.valid_to as ValidTo
            from resolutions r
            where r.company_id = @CompanyId and r.is_active = {dialect.Bool(true)}
            """;
        await using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<ResolutionRow>(new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: ct));
        var sequences = (await connection.QueryAsync<SequenceRow>(
                new CommandDefinition(ResolutionSequencesSql, new { CompanyId = companyId }, cancellationToken: ct)))
            .ToDictionary(sequence => sequence.ScopeKey, sequence => sequence.NextNumber, StringComparer.Ordinal);
        return rows
            .Select(row => new DashboardResolution(
                row.Id, row.DocumentTypeId, DocumentKinds.NameOf(row.DocumentTypeId), row.Prefix, row.ResolutionNumber, row.RangeFrom, row.RangeTo,
                sequences.TryGetValue(NumberingScope.ForResolution(row.Id), out var next) ? next : null,
                row.ValidTo is { } validTo ? DateOnly.FromDateTime(validTo) : null))
            .ToList();
    }

    private static string PeriodName(PlanPeriod period) => period switch
    {
        PlanPeriod.Monthly => "monthly",
        PlanPeriod.Yearly => "yearly",
        PlanPeriod.Package => "package",
        _ => "unlimited",
    };

    // Filas tal como las devuelve el SQL. Npgsql entrega timestamptz como DateTime en UTC y date como DateTime.

    private sealed class CountRow
    {
        public string Month { get; init; } = "";

        public int DocumentTypeId { get; init; }

        public short Status { get; init; }

        public long Total { get; init; }
    }

    private sealed class CertificateRow
    {
        public string Name { get; init; } = "";

        public DateTime NotAfter { get; init; }
    }

    private sealed class ResolutionRow
    {
        public Guid Id { get; init; }

        public int DocumentTypeId { get; init; }

        public string? Prefix { get; init; }

        public string? ResolutionNumber { get; init; }

        public long RangeFrom { get; init; }

        public long RangeTo { get; init; }

        public DateTime? ValidTo { get; init; }
    }

    private sealed class SequenceRow
    {
        public string ScopeKey { get; init; } = "";

        public long NextNumber { get; init; }
    }
}
