using System.ComponentModel.DataAnnotations;

namespace ImpiFact.Infrastructure.Options;

/// <summary>
/// Tokens JWT (JSON Web Token: credencial firmada que el panel envía en cada solicitud) de los usuarios del
/// panel (Arquitectura detallada, secciones 9.2 y 11). La llave de firma es un secreto y llega en la tarea de
/// Identity, por variable de entorno, nunca en <c>appsettings.json</c>.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Quién emite el token.</summary>
    [Required]
    public string Issuer { get; set; } = "";

    /// <summary>Para quién es el token (la aplicación que lo acepta).</summary>
    [Required]
    public string Audience { get; set; } = "";

    /// <summary>Vigencia del token de acceso, en minutos.</summary>
    [Range(1, 1440)]
    public int AccessMinutes { get; set; }

    /// <summary>Vigencia del token de renovación (el que permite pedir uno de acceso nuevo), en días.</summary>
    [Range(1, 365)]
    public int RefreshDays { get; set; }
}
