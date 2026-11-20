using ImpiFact.Domain.Companies;

namespace ImpiFact.Application.Abstractions.Companies;

/// <summary>
/// Acceso de escritura a las tablas del módulo Company (Modelo de datos detallado, sección 3.3). Los casos de uso
/// leen las filas que van a cambiar, las cambian en memoria y el decorador de transacción guarda todo al final:
/// nadie llama a <c>SaveChanges</c> desde aquí.
/// </summary>
/// <remarks>
/// Los métodos que reciben <c>companyId</c> buscan solo en esa empresa, también cuando quien llama es la plataforma
/// (que no tiene empresa en el contexto y por eso no vería ninguna fila con el filtro de tenant). El caso de uso
/// pasa siempre una empresa de fuente confiable: la de la credencial o la que encontró por NIT un actor de
/// plataforma. La guardia de escritura de la base sigue impidiendo que una empresa escriba filas de otra.
/// </remarks>
public interface ICompanyRepository
{
    Task<Company?> FindByIdAsync(Guid companyId, CancellationToken ct);

    /// <summary>Busca la empresa por su NIT (sin dígito de verificación). Solo la plataforma busca por NIT.</summary>
    Task<Company?> FindByNitAsync(string nit, CancellationToken ct);

    /// <summary>
    /// Indica si otra empresa ya usa el correo, sin distinguir mayúsculas. <paramref name="exceptCompanyId"/> excluye
    /// a la empresa que se está actualizando.
    /// </summary>
    Task<bool> IsEmailTakenAsync(string email, Guid? exceptCompanyId, CancellationToken ct);

    Task<IReadOnlyList<CompanyLiability>> GetLiabilitiesAsync(Guid companyId, CancellationToken ct);

    Task<IReadOnlyList<CompanyEnvironment>> GetEnvironmentsAsync(Guid companyId, CancellationToken ct);

    Task<IReadOnlyList<CompanySoftware>> GetSoftwareAsync(Guid companyId, CancellationToken ct);

    Task<Certificate?> GetActiveCertificateAsync(Guid companyId, CancellationToken ct);

    /// <summary>Resoluciones activas e inactivas de un tipo de documento en un ambiente.</summary>
    Task<IReadOnlyList<Resolution>> GetResolutionsAsync(Guid companyId, int documentTypeId, int typeEnvironmentId, CancellationToken ct);

    /// <summary>Una resolución de la empresa por su id; nula si no existe o es de otra empresa.</summary>
    Task<Resolution?> FindResolutionAsync(Guid companyId, Guid resolutionId, CancellationToken ct);

    /// <summary>Agrega una fila nueva; se inserta cuando el decorador de transacción guarda los cambios.</summary>
    void Add<TEntity>(TEntity entity)
        where TEntity : class;

    /// <summary>Marca una fila para borrarla al guardar.</summary>
    void Remove<TEntity>(TEntity entity)
        where TEntity : class;
}
