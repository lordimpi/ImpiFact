namespace ImpiFact.Application.Abstractions.Identity;

/// <summary>Plazos de las sesiones del panel que no dependen del token de acceso (Arquitectura detallada, sección 9.2).</summary>
public interface ISessionPolicy
{
    /// <summary>
    /// Plazo propio de una revocación que no usa la cancelación de la petición: el cierre de sesión y la respuesta a un
    /// token de renovación reusado (10 segundos). Si se cumple, la API responde el mismo 503
    /// (<c>identity.store_unavailable</c>) que ante una caída de la base (<see cref="Features.Identity.Sessions.SessionRevocation"/>).
    /// </summary>
    TimeSpan RevocationTimeout { get; }

    /// <summary>
    /// Período de gracia de la rotación del token de renovación (<c>Jwt:RefreshRotationGraceSeconds</c>, 30 segundos por
    /// defecto; cero lo apaga). Un token ya rotado que vuelve antes de que pase este tiempo, con su reemplazo todavía sin
    /// usar, se toma como una respuesta perdida y no como un robo (<see cref="Features.Identity.Sessions.RefreshTokenHandler"/>).
    /// </summary>
    TimeSpan RotationGracePeriod { get; }
}
