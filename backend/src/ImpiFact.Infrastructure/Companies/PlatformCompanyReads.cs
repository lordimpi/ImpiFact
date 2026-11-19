using System.Text;
using Dapper;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Infrastructure.Masters;
using ImpiFact.Infrastructure.Persistence;

namespace ImpiFact.Infrastructure.Companies;

/// <summary>
/// Listado de empresas de la plataforma con Dapper (una librería que ejecuta SQL escrito a mano). <c>companies</c> no
/// tiene <c>company_id</c>: es la tabla de las empresas, y este listado es de la plataforma, que las ve todas. El plan
/// vigente es el contratado activo más reciente, la misma elección de <c>EfPlanStore.FindCurrentAsync</c>, buscado con
/// el índice <c>ix_company_plans_company_id_is_active</c>.
/// </summary>
/// <remarks>
/// Ordena por <c>lower(name)</c> y luego por id para que mayúsculas y minúsculas no separen las empresas; el cursor
/// guarda ese mismo valor, calculado por la base. La búsqueda no distingue mayúsculas y escapa los comodines del texto.
/// El SQL es común a los tres motores; la búsqueda, el plan vigente (una subconsulta por empresa), el límite y los
/// booleanos los pone el dialecto del motor (<see cref="ISqlDialect"/>).
/// </remarks>
internal sealed class PlatformCompanyReads(IReadConnectionFactory connections, ISqlDialect dialect) : IPlatformCompanyReads
{
    public async Task<IReadOnlyList<PlatformCompanyRow>> ListAsync(PlatformCompanyFilter filter, CancellationToken ct)
    {
        var parameters = new DynamicParameters(new { filter.Take });
        // El plan vigente va en la misma cadena que la consulta para que la regla «toda consulta lleva company_id» (una
        // prueba de arquitectura) lea la condición de la subconsulta.
        var sql = new StringBuilder($"""
            select c.id as Id, c.name as Name, lower(c.name) as SortName, c.identification_number as Nit, c.dv as Dv,
                   c.email as Email, c.is_active as IsActive, p.id as PlanId, p.name as PlanName
            from companies c
            {dialect.OuterApply(
                $"select cp.plan_id from company_plans cp where cp.company_id = c.id and cp.is_active = {dialect.Bool(true)} " +
                $"order by cp.starts_at desc, cp.id desc {dialect.Paging("1")}",
                "current_plan")}
            left join plans p on p.id = current_plan.plan_id
            where 1 = 1
            """);
        if (filter.Search is { } search)
        {
            sql.Append($" and ({dialect.LikeIgnoreCase("c.identification_number", "@Pattern")} or {dialect.LikeIgnoreCase("c.name", "@Pattern")})");
            parameters.Add("Pattern", MasterSql.ContainsPattern(search));
        }

        if (filter.After is { } after)
        {
            MasterSql.AppendAfter(sql, "lower(c.name)", "c.id", ">", "@AfterName", "@AfterId");
            parameters.Add("AfterName", after.SortName);
            parameters.Add("AfterId", after.Id);
        }

        sql.Append($" order by lower(c.name), c.id {dialect.Paging("@Take")}");
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
