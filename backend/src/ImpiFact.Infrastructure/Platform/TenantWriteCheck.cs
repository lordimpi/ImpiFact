using ImpiFact.Application.Abstractions;

namespace ImpiFact.Infrastructure.Platform;

/// <summary>
/// La misma regla de la guardia de escritura por empresa (<c>TenantWriteGuardInterceptor</c>; tenant: cada empresa
/// cliente con sus datos aislados) para las escrituras que no pasan por esa guardia: el UPDATE directo del
/// consecutivo y los mensajes de la bandeja de salida, que no son filas de empresa. Una solicitud de empresa solo
/// escribe para su propia empresa; la plataforma y los procesos internos pueden escribir para cualquiera.
/// </summary>
internal static class TenantWriteCheck
{
    /// <exception cref="InvalidOperationException">Si la solicitud es de una empresa distinta de <paramref name="companyId"/>.</exception>
    public static void EnsureCanWrite(ITenantContext tenant, Guid? companyId, string what)
    {
        if (tenant.Actor is ActorType.Platform or ActorType.System)
        {
            return;
        }

        if (tenant.CompanyId is not { } company || companyId != company)
        {
            throw new InvalidOperationException($"{what} does not belong to the company of the current request.");
        }
    }
}
