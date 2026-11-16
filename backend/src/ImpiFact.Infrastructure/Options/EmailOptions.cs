using System.ComponentModel.DataAnnotations;

namespace ImpiFact.Infrastructure.Options;

/// <summary>
/// Servidor de correo (SMTP, el protocolo de envío de correo) por defecto de la plataforma (sección «Email»;
/// Arquitectura detallada, sección 11): con él sale el correo de las empresas que no tienen uno propio en
/// <c>company_mail_settings</c>. Solo lo usa el Worker, que lo valida al arrancar.
/// </summary>
/// <remarks>
/// El usuario y la contraseña son secretos: llegan por variable de entorno (<c>Email__Username</c>,
/// <c>Email__Password</c>) o por el gestor de secretos, nunca por <c>appsettings.json</c>. Un servidor sin
/// autenticación (por ejemplo, el capturador de correo de desarrollo) los deja vacíos.
/// </remarks>
public sealed class EmailOptions : IValidatableObject
{
    public const string SectionName = "Email";

    public const string NoEncryption = "none";
    public const string StartTls = "starttls";
    public const string SslOnConnect = "ssl";
    public const string AutoEncryption = "auto";

    /// <summary>Nombre o dirección del servidor SMTP.</summary>
    [Required]
    public string Host { get; set; } = "";

    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    /// <summary>
    /// Cifrado de la conexión: <c>none</c> (sin cifrar), <c>starttls</c> (empieza sin cifrar y pasa a TLS),
    /// <c>ssl</c> (TLS desde el inicio) o <c>auto</c> (lo decide la librería).
    /// </summary>
    [Required]
    [AllowedValues(NoEncryption, StartTls, SslOnConnect, AutoEncryption)]
    public string Encryption { get; set; } = StartTls;

    /// <summary>Usuario SMTP; vacío si el servidor no pide autenticación.</summary>
    public string? Username { get; set; }

    /// <summary>Contraseña SMTP; secreto, nunca se registra.</summary>
    public string? Password { get; set; }

    /// <summary>Dirección del remitente por defecto.</summary>
    [Required]
    [EmailAddress]
    public string FromAddress { get; set; } = "";

    /// <summary>Nombre del remitente por defecto.</summary>
    public string? FromName { get; set; }

    /// <summary>Tiempo máximo de cada operación con el servidor de correo, en segundos.</summary>
    [Range(1, 600)]
    public int TimeoutSeconds { get; set; } = 60;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Username) != string.IsNullOrEmpty(Password))
        {
            yield return new ValidationResult(
                $"{SectionName}:{nameof(Username)} and {SectionName}:{nameof(Password)} go together: set both or neither.",
                [nameof(Username), nameof(Password)]);
        }
    }
}
