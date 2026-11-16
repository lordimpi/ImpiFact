using ImpiFact.Domain.Common;

namespace ImpiFact.Domain.Identity;

// Plataforma e identidad (Modelo de datos detallado, sección 3.2). Las tablas son propias, con nombres en
// snake_case; no se usan los almacenes de ASP.NET Identity (supuesto A4 de la fase 2).

/// <summary>Revendedor de la plataforma (<c>resellers</c>; en el PHP, <c>administrators</c>).</summary>
public sealed class Reseller : PlatformEntity
{
    /// <summary>NIT sin dígito de verificación, solo dígitos.</summary>
    public required string IdentificationNumber { get; set; }

    /// <summary>Dígito de verificación del NIT.</summary>
    public string? Dv { get; set; }

    public required string Name { get; set; }

    public string? Address { get; set; }

    public string? Phone { get; set; }

    /// <summary>Correo en minúsculas.</summary>
    public required string Email { get; set; }

    public string? ContactName { get; set; }

    public string? PlanLabel { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Observation { get; set; }
}

/// <summary>
/// Usuario del panel o del portal del cliente (<c>users</c>). Sin token de API ni datos de correo: esos se movieron
/// a <c>api_keys</c> y <c>company_mail_settings</c>.
/// </summary>
public sealed class User : PlatformEntity
{
    /// <summary>Correo normalizado en minúsculas; es único.</summary>
    public required string Email { get; set; }

    public required string PasswordHash { get; set; }

    public required string Name { get; set; }

    public string? Phone { get; set; }

    public Guid? ResellerId { get; set; }

    public bool IsActive { get; set; } = true;

    public bool EmailConfirmed { get; set; }

    /// <summary>Hasta cuándo está bloqueado el inicio de sesión tras varios intentos fallidos.</summary>
    public DateTimeOffset? LockoutEnd { get; set; }

    public int AccessFailedCount { get; set; }

    /// <summary>Valor que cambia cuando cambian las credenciales; invalida las sesiones anteriores.</summary>
    public required string SecurityStamp { get; set; }
}

/// <summary>Rol (<c>roles</c>). Los cinco roles se siembran en la migración con llaves fijas.</summary>
public sealed class Role
{
    public Guid Id { get; init; }

    public required string Name { get; init; }
}

/// <summary>Nombres de los roles sembrados (Arquitectura detallada, sección 7).</summary>
public static class RoleNames
{
    public const string PlatformAdmin = "platform_admin";

    public const string Reseller = "reseller";

    public const string Owner = "owner";

    public const string Seller = "seller";

    public const string Customer = "customer";
}

/// <summary>Rol de plataforma de un usuario (<c>user_roles</c>), con llave compuesta.</summary>
public sealed class UserRole
{
    public Guid UserId { get; init; }

    public Guid RoleId { get; init; }
}

/// <summary>
/// Pertenencia de un usuario a una empresa con un rol (<c>user_companies</c>; en el PHP, <c>company_user</c>).
/// Lleva el filtro de empresa: para listar las empresas de un usuario antes de elegir una, se pide a propósito
/// sin ese filtro.
/// </summary>
public sealed class UserCompany : TenantEntity
{
    public Guid UserId { get; init; }

    public Guid RoleId { get; init; }

    /// <summary>Cliente de la empresa; solo para el rol <c>customer</c> (portal del cliente).</summary>
    public Guid? CustomerId { get; set; }
}

/// <summary>
/// API key (<c>api_keys</c>). La clave en claro se muestra una sola vez al crearla; solo se guarda su resumen
/// SHA-256 (<see cref="KeyHash"/>). Sin empresa es una llave de plataforma, que reemplaza al <c>X-Admin-Token</c>.
/// </summary>
/// <remarks>
/// No lleva versión de fila: <see cref="LastUsedAt"/> se actualiza en cada solicitud y una versión haría fallar
/// solicitudes simultáneas con la misma llave.
/// </remarks>
public sealed class ApiKey : IAuditable
{
    public Guid Id { get; init; }

    public Guid? CompanyId { get; init; }

    public required string Name { get; set; }

    /// <summary>Inicio visible de la clave (por ejemplo <c>apk_1a2b3c4d</c>) para reconocerla en el panel; no es secreto.</summary>
    public required string KeyPrefix { get; init; }

    /// <summary>SHA-256 de la clave en hexadecimal (64 caracteres).</summary>
    public required string KeyHash { get; init; }

    /// <summary>Permisos separados por coma: <c>documents:write</c>, <c>documents:read</c>, <c>config:write</c>…</summary>
    public required string Scopes { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Usuario que la creó; nulo si la creó el sistema (la primera llave de plataforma, desde el Migrator).</summary>
    public Guid? CreatedBy { get; init; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}

/// <summary>Token de renovación de sesión (<c>refresh_tokens</c>); solo se guarda su resumen SHA-256.</summary>
public sealed class RefreshToken : PlatformEntity
{
    public Guid UserId { get; init; }

    public required string TokenHash { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Token que lo reemplazó al rotar.</summary>
    public Guid? ReplacedBy { get; set; }

    /// <summary>IP de origen (hasta 45 caracteres, el largo de una IPv6).</summary>
    public string? CreatedIp { get; init; }

    public string? UserAgent { get; init; }
}
