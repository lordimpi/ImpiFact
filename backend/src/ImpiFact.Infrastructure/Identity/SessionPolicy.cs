using ImpiFact.Application.Abstractions.Identity;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>Plazos de las sesiones del panel (<see cref="ISessionPolicy"/>).</summary>
internal sealed class SessionPolicy : ISessionPolicy
{
    /// <summary>10 segundos: de sobra para una revocación, y la petición no queda colgada si la base no contesta.</summary>
    public TimeSpan RevocationTimeout { get; } = TimeSpan.FromSeconds(10);
}
