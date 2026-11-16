using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Notifications;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Persistence;

namespace ImpiFact.Infrastructure.Platform;

/// <summary>
/// Avisos del panel (<c>notifications</c>) sobre el contexto de EF Core (Entity Framework) del ámbito: la fila se
/// guarda junto con el resto de los cambios del caso de uso o del handler.
/// </summary>
internal sealed class EfCompanyNotifications(ImpiFactDbContext db, ITenantContext tenant) : ICompanyNotifications
{
    public void Add(Guid companyId, string type, string title, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        TenantWriteCheck.EnsureCanWrite(tenant, companyId, nameof(Notification));
        db.Set<Notification>().Add(new Notification { CompanyId = companyId, Type = type, Title = title, Body = body });
    }
}
