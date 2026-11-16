using ImpiFact.Application.Abstractions;

namespace ImpiFact.Infrastructure.Tenancy;

/// <summary>
/// Implementación de <see cref="ITenantContext"/> (tenant: cada empresa cliente con sus datos aislados) con una
/// instancia por solicitud. Empieza anónima, sin empresa; el middleware de la API (tarea de Identity) o el Worker
/// la fijan con <see cref="Set"/> antes de tocar la base de datos.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    public Guid? CompanyId { get; private set; }

    public Guid? UserId { get; private set; }

    public ActorType Actor { get; private set; } = ActorType.Anonymous;

    public IReadOnlySet<string> Scopes { get; private set; } = new HashSet<string>();

    public void Set(Guid? companyId, Guid? userId, ActorType actor, IReadOnlySet<string>? scopes = null)
    {
        CompanyId = companyId;
        UserId = userId;
        Actor = actor;
        Scopes = scopes ?? new HashSet<string>();
    }
}
