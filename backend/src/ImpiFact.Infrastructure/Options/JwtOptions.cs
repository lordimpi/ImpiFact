using System.ComponentModel.DataAnnotations;

namespace ImpiFact.Infrastructure.Options;

/// <summary>
/// Tokens JWT (JSON Web Token: credencial firmada que el panel envía en cada solicitud) de los usuarios del
/// panel (Arquitectura detallada, secciones 9.2 y 11). La llave de firma es un secreto: llega por variable de
/// entorno (<c>Jwt__SigningKey</c>) o por el proveedor de secretos, nunca en <c>appsettings.json</c>.
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

    /// <summary>
    /// Secreto: llave privada ECDSA (firma digital con curvas elípticas) de la curva P-256, en formato PEM (texto
    /// que empieza con <c>-----BEGIN PRIVATE KEY-----</c>). Con ella se firman los tokens con ES256 (ECDSA P-256 con
    /// SHA-256). Fuera de Development es obligatoria y el arranque falla sin ella; en Development, si falta, se
    /// genera una llave temporal que se pierde al reiniciar. Para crear una:
    /// <c>openssl ecparam -name prime256v1 -genkey -noout | openssl pkcs8 -topk8 -nocrypt</c>.
    /// </summary>
    public string? SigningKey { get; set; }

    /// <summary>
    /// Llaves públicas anteriores en PEM (<c>-----BEGIN PUBLIC KEY-----</c>) que todavía validan tokens durante una
    /// rotación: al cambiar <see cref="SigningKey"/>, la pública de la llave vieja se deja aquí al menos lo que dura
    /// un token de acceso y luego se quita. No son secretos.
    /// </summary>
    public string[] PreviousPublicKeys { get; set; } = [];
}
