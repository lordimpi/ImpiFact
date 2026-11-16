using ImpiFact.Domain.Identity;

namespace ImpiFact.Infrastructure.Persistence.Seeding;

/// <summary>
/// Semilla de plataforma (Modelo de datos detallado, sección 9): los cinco roles, con llaves fijas para que la
/// migración y el código usen siempre los mismos valores. Los tipos de ambiente vienen del catálogo
/// <c>type_environments</c>. No hay planes base: el modelo no define sus nombres ni cuotas.
/// </summary>
internal static class PlatformSeed
{
    public static readonly Guid PlatformAdminRoleId = new("0193a000-0000-7000-8000-000000000001");

    public static readonly Guid ResellerRoleId = new("0193a000-0000-7000-8000-000000000002");

    public static readonly Guid OwnerRoleId = new("0193a000-0000-7000-8000-000000000003");

    public static readonly Guid SellerRoleId = new("0193a000-0000-7000-8000-000000000004");

    public static readonly Guid CustomerRoleId = new("0193a000-0000-7000-8000-000000000005");

    public static IReadOnlyList<Role> Roles { get; } =
    [
        new() { Id = PlatformAdminRoleId, Name = RoleNames.PlatformAdmin },
        new() { Id = ResellerRoleId, Name = RoleNames.Reseller },
        new() { Id = OwnerRoleId, Name = RoleNames.Owner },
        new() { Id = SellerRoleId, Name = RoleNames.Seller },
        new() { Id = CustomerRoleId, Name = RoleNames.Customer },
    ];
}
