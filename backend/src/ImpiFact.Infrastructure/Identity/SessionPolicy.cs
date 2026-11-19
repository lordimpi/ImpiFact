using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Infrastructure.Options;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>Plazos de las sesiones del panel (<see cref="ISessionPolicy"/>), con el período de gracia de la sección «Jwt».</summary>
internal sealed class SessionPolicy(IOptionsService<JwtOptions> options) : ISessionPolicy
{
    /// <summary>10 segundos: de sobra para una revocación, y la petición no queda colgada si la base no contesta.</summary>
    public TimeSpan RevocationTimeout { get; } = TimeSpan.FromSeconds(10);

    public TimeSpan RotationGracePeriod => TimeSpan.FromSeconds(options.Value.RefreshRotationGraceSeconds);
}
