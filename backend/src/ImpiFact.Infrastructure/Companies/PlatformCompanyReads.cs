using System.Text;
using Dapper;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Infrastructure.Masters;

namespace ImpiFact.Infrastructure.Companies;

/// <summary>
/// Listado de empresas de la plataforma con Dapper (una librería que ejecuta SQL escrito a mano). <c>companies</c> no
/// tiene <c>company_id</c>: es la tabla de las empresas, y este listado es de la plataforma, que las ve todas. El plan
/// vigente es el contratado activo más reciente, la misma elección de <c>EfPlanStore.FindCurrentAsync</c>, buscado con
/// el índice <c>ix_company_plans_company_id_is_active</c>.
/// </summary>
/// <remarks>
/// Ordena por <c>lower(name)</c> y luego por id para que mayúsculas y minúsculas no separen las empresas; el cursor
/// guarda ese mismo valor, calculado por la base. La búsqueda usa ILIKE con los comodines del texto escapados.
/// </remarks>
internal sealed class PlatformCompanyReads(IReadConnectionFactory connections) : IPlatformCompanyReads
{
    public async Task<IReadOnlyList<PlatformCompanyRow>> ListAsync(PlatformCompanyFilter filter, CancellationToken ct)
    {
        var parameters = new DynamicParameters(new { filter.Take });
        var sql = new StringBuilder("""
            select c.id as Id, c.name as Name, lower(c.name) as SortName, c.identification_number as Nit, c.dv as Dv,
                   c.email as Email, c.is_active as IsActive, p.id as PlanId, p.name as PlanName
            from companies c
            left join lateral (
                select cp.plan_id from company_plans cp
                where cp.company_id = c.id and cp.is_active
                order by cp.starts_at desc, cp.id desc
                limit 1
            ) current_plan on true
            left join plans p on p.id = current_plan.plan_id
            where true
            """);
        if (filter.Search is { } search)
        {
            sql.Append(" and (c.identification_number ilike @Pattern or c.name ilike @Pattern)");
            parameters.Add("Pattern", MasterSql.ContainsPattern(search));
        }

        if (filter.After is { } after)
        {
            sql.Append(" and (lower(c.name), c.id) > (@AfterName, @AfterId)");
            parameters.Add("AfterName", after.SortName);
            parameters.Add("AfterId", after.Id);
        }

        sql.Append(" order by lower(c.name), c.id limit @Take");
        await using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(sql.ToString(), parameters, cancellationToken: ct));
        return rows
            .Select(row => new PlatformCompanyRow(row.Id, row.Name, row.SortName, row.Nit, row.Dv, row.Email, row.IsActive, row.PlanId, row.PlanName))
            .ToList();
    }

    private sealed class Row
    {
        public Guid Id { get; init; }

        public string Name { get; init; } = "";

        public string SortName { get; init; } = "";

        public string Nit { get; init; } = "";

        public string Dv { get; init; } = "";

        public string Email { get; init; } = "";

        public bool IsActive { get; init; }

        public int? PlanId { get; init; }

        public string? PlanName { get; init; }
    }
}
