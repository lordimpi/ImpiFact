namespace ImpiFact.Application.Abstractions.Companies;

/// <summary>
/// Listado de todas las empresas para la pantalla de la plataforma (<c>/plataforma/empresas</c>), con el nombre del
/// plan vigente de cada una. El plan vive en el módulo Billing (<c>company_plans</c>, <c>plans</c>), por eso el módulo
/// Company lo pide por este puerto. Solo lo usa la plataforma: no filtra por empresa.
/// </summary>
public interface IPlatformCompanyReads
{
    /// <summary>
    /// Empresas ordenadas por razón social en minúsculas y luego por id, después de <see cref="PlatformCompanyFilter.After"/>.
    /// </summary>
    Task<IReadOnlyList<PlatformCompanyRow>> ListAsync(PlatformCompanyFilter filter, CancellationToken ct);
}

/// <summary>Posición de una empresa en el orden del listado: la razón social en minúsculas (como la ordena la base) y el id.</summary>
public readonly record struct PlatformCompanyPosition(string SortName, Guid Id);

/// <summary>Una página pedida al listado de empresas.</summary>
/// <param name="Search">Texto que contiene el NIT o la razón social, sin distinguir mayúsculas; nulo para no filtrar.</param>
/// <param name="After">Última empresa de la página anterior; nula para la primera página.</param>
/// <param name="Take">Máximo de filas que devuelve la consulta.</param>
public sealed record PlatformCompanyFilter(string? Search, PlatformCompanyPosition? After, int Take);

/// <summary>Una empresa del listado de la plataforma.</summary>
/// <param name="SortName">Razón social en minúsculas, tal como la ordenó la base; sirve para el cursor.</param>
/// <param name="Nit">NIT (número de identificación tributaria) sin dígito de verificación.</param>
/// <param name="PlanId">Plan contratado activo más reciente; nulo si no tiene.</param>
public sealed record PlatformCompanyRow(
    Guid Id, string Name, string SortName, string Nit, string Dv, string Email, bool IsActive, int? PlanId, string? PlanName);
