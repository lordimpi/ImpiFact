namespace ImpiFact.Application.Abstractions.Identity;

/// <summary>
/// Puerto de solo lectura de las empresas a las que entra una credencial, con los datos que muestra el selector de
/// empresa del panel. Los nombres viven en el módulo Company (<c>companies</c>), por eso Identity los pide por aquí
/// en lugar de leer esa tabla. Lo implementa Infrastructure con una consulta sin rastreo.
/// </summary>
public interface IUserCompanyReads
{
    /// <summary>
    /// Empresas en las que el usuario tiene al menos un rol (<c>user_companies</c>), activas o no, con todos sus roles
    /// en cada una, ordenadas por razón social y luego por id. Se leen sin el filtro de empresa a propósito: son las
    /// que el usuario puede elegir antes de trabajar en una.
    /// </summary>
    Task<IReadOnlyList<AccessibleCompany>> ForUserAsync(Guid userId, CancellationToken ct);

    /// <returns>La empresa con ese id, sin roles; <see langword="null"/> si no existe.</returns>
    Task<AccessibleCompany?> ByIdAsync(Guid companyId, CancellationToken ct);
}

/// <summary>Una empresa tal como la ve el selector de empresa.</summary>
/// <param name="Name">Razón social (<c>companies.name</c>).</param>
/// <param name="Nit">NIT (número de identificación tributaria) sin dígito de verificación.</param>
/// <param name="Dv">Dígito de verificación del NIT.</param>
/// <param name="Roles">Roles de quien llama en esa empresa, sin repetir y en orden alfabético.</param>
public sealed record AccessibleCompany(Guid Id, string Name, string Nit, string Dv, bool IsActive, IReadOnlyList<string> Roles);
