namespace ImpiFact.Application.Abstractions.Identity;

/// <summary>
/// Puerto de los usuarios del panel y sus roles (tablas <c>users</c>, <c>user_roles</c> y <c>user_companies</c>).
/// Los correos se pasan ya normalizados: sin espacios alrededor y en minúsculas.
/// </summary>
public interface IUserStore
{
    /// <returns>El usuario con ese correo, o <see langword="null"/> si no existe.</returns>
    Task<UserAccount?> FindByEmailAsync(string normalizedEmail, CancellationToken ct);

    /// <returns>El usuario con ese id, o <see langword="null"/> si no existe.</returns>
    Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken ct);

    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct);

    /// <summary>Si existe la empresa (sin el filtro de empresa: la usa la plataforma al crear su primer dueño).</summary>
    Task<bool> CompanyExistsAsync(Guid companyId, CancellationToken ct);

    /// <summary>
    /// Roles de plataforma del usuario (<c>user_roles</c>), por ejemplo <c>platform_admin</c>, y sus empresas con
    /// el rol en cada una (<c>user_companies</c>). Las empresas se leen sin el filtro de empresa a propósito: se
    /// necesitan antes de saber en qué empresa va a trabajar el usuario.
    /// </summary>
    Task<UserRoles> GetRolesAsync(Guid userId, CancellationToken ct);

    /// <summary>Cambia el resumen de la contraseña (por ejemplo, al pasar a un algoritmo más fuerte).</summary>
    Task UpdatePasswordHashAsync(Guid userId, string passwordHash, CancellationToken ct);

    /// <summary>
    /// Cambia la contraseña porque el usuario eligió otra (por ejemplo, al restablecerla): guarda el resumen nuevo y
    /// cambia el sello de seguridad (<c>security_stamp</c>). Se guarda al confirmar la transacción del caso de uso.
    /// </summary>
    Task ChangePasswordAsync(Guid userId, string passwordHash, CancellationToken ct);

    /// <summary>Agrega un usuario con sus roles. Se guarda al confirmar la transacción del caso de uso.</summary>
    Task AddAsync(NewUser user, CancellationToken ct);
}

/// <summary>Un usuario tal como lo necesita el inicio de sesión.</summary>
/// <param name="PasswordHash">Resumen de la contraseña; nunca se escribe en los registros ni se devuelve.</param>
/// <param name="LockoutEnd">Hasta cuándo está bloqueado el inicio de sesión; nulo si no lo está.</param>
public sealed record UserAccount(Guid Id, string Email, string PasswordHash, bool IsActive, DateTimeOffset? LockoutEnd)
{
    /// <summary>No incluye el resumen de la contraseña, para que un registro accidental del objeto no lo filtre.</summary>
    public override string ToString() => $"UserAccount {{ Id = {Id}, IsActive = {IsActive} }}";
}

/// <summary>Roles de un usuario: los de plataforma y uno por cada empresa a la que pertenece.</summary>
public sealed record UserRoles(IReadOnlyList<string> PlatformRoles, IReadOnlyList<CompanyMembership> Companies)
{
    /// <summary>Roles del usuario en una empresa (puede tener más de uno).</summary>
    public IReadOnlyList<string> RolesIn(Guid companyId) =>
        [.. Companies.Where(membership => membership.CompanyId == companyId).Select(membership => membership.Role).Distinct()];

    /// <summary>Empresas distintas a las que pertenece.</summary>
    public IReadOnlyList<Guid> CompanyIds => [.. Companies.Select(membership => membership.CompanyId).Distinct()];
}

/// <param name="Role">Nombre del rol en esa empresa, por ejemplo <c>owner</c>.</param>
public sealed record CompanyMembership(Guid CompanyId, string Role);

/// <summary>Un usuario nuevo con sus roles.</summary>
/// <param name="Email">Correo ya normalizado.</param>
/// <param name="PasswordHash">Resumen de la contraseña; nunca la contraseña.</param>
/// <param name="PlatformRoles">Roles de plataforma (<c>user_roles</c>).</param>
/// <param name="Company">Empresa y rol en ella (<c>user_companies</c>); nulo si el usuario no pertenece a ninguna.</param>
public sealed record NewUser(
    Guid Id,
    string Email,
    string PasswordHash,
    string Name,
    string? Phone,
    IReadOnlyList<string> PlatformRoles,
    CompanyMembership? Company)
{
    /// <summary>No incluye el resumen de la contraseña.</summary>
    public override string ToString() => $"NewUser {{ Id = {Id} }}";
}
