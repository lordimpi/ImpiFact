namespace ImpiFact.Application.Abstractions;

/// <summary>
/// Quién hace la solicitud y para qué empresa (tenant: cada empresa cliente con sus datos aislados).
/// El middleware de la API lo llena desde la API key o el token JWT; el Worker lo fija antes de procesar
/// cada mensaje (Arquitectura detallada, sección 4.4).
/// </summary>
public interface ITenantContext
{
    /// <summary>Empresa sobre la que se actúa; nula si la solicitud no es de una empresa.</summary>
    Guid? CompanyId { get; }

    /// <summary>Usuario del panel que hace la solicitud; nulo si llega con API key o desde el Worker.</summary>
    Guid? UserId { get; }

    ActorType Actor { get; }
}

/// <summary>Tipo de quien hace la solicitud.</summary>
public enum ActorType
{
    /// <summary>Sin credenciales, por ejemplo antes de iniciar sesión.</summary>
    Anonymous,

    /// <summary>Usuario del panel, autenticado con JWT.</summary>
    User,

    /// <summary>Integración de una empresa, autenticada con su API key.</summary>
    ApiKey,

    /// <summary>API key de plataforma, sin empresa, para administrar empresas, planes y revendedores.</summary>
    Platform,

    /// <summary>Procesos internos, como el Worker.</summary>
    System,
}
