namespace ImpiFact.Application.Abstractions.Email;

/// <summary>
/// Servidor de correo propio de una empresa (<c>company_mail_settings</c>; Modelo de datos detallado, sección 3.3). Es
/// un puerto de solo lectura porque Documents no puede usar las entidades del módulo Company; lo implementa
/// Infrastructure con una consulta sin rastreo que pone la empresa en el WHERE.
/// </summary>
public interface ICompanyMailSettingsReads
{
    /// <summary>Servidor de correo de la empresa; nulo si la empresa no configuró uno.</summary>
    Task<CompanyMailServerSettings?> GetAsync(Guid companyId, CancellationToken ct);
}

/// <summary>Fila de <c>company_mail_settings</c>. La contraseña llega cifrada: solo el caso de uso la descifra para enviar.</summary>
/// <param name="UsePlatformDefault">La empresa eligió el servidor de la plataforma aunque tenga uno propio.</param>
/// <param name="Encryption">Cifrado como lo guarda el PHP, por ejemplo <c>tls</c> o <c>ssl</c>.</param>
public sealed record CompanyMailServerSettings(
    bool UsePlatformDefault,
    string? Host,
    int? Port,
    string? Username,
    string? PasswordCipher,
    string? Encryption,
    string? FromAddress,
    string? FromName)
{
    /// <summary>
    /// True when the email of the company goes out through this server: it is complete (host, port, user, password and
    /// encryption, as <c>validate_mail_server</c> of ApiDian) and the company did not choose the platform server.
    /// </summary>
    public bool SendsThroughCompany =>
        !UsePlatformDefault
        && !string.IsNullOrWhiteSpace(Host)
        && Port > 0
        && !string.IsNullOrWhiteSpace(Username)
        && !string.IsNullOrEmpty(PasswordCipher)
        && !string.IsNullOrWhiteSpace(Encryption);
}

/// <summary>
/// Servidor de correo por defecto de la plataforma (sección «Email» de la configuración), con que sale el correo de
/// las empresas que no tienen uno propio. La contraseña llega por variable de entorno o por el gestor de secretos.
/// </summary>
public interface IPlatformMailServer
{
    MailServer Default { get; }
}
