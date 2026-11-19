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
}
