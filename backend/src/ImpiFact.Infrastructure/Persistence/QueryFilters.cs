namespace ImpiFact.Infrastructure.Persistence;

/// <summary>
/// Nombres de los filtros globales de consulta (condiciones que EF Core agrega a toda consulta de la entidad).
/// Tienen nombre para poder quitar uno solo, por ejemplo
/// <c>IgnoreQueryFilters([QueryFilters.SoftDelete])</c> para ver filas borradas sin dejar de filtrar por empresa.
/// </summary>
public static class QueryFilters
{
    public const string Tenant = "Tenant";

    public const string SoftDelete = "SoftDelete";
}
