namespace ImpiFact.Application.Abstractions.Email;

/// <summary>
/// Envía correos por SMTP (el protocolo de envío de correo) con el servidor que recibe en cada llamada: el de la
/// empresa o el de la plataforma (Arquitectura detallada, sección 5). Lo implementa Infrastructure con MailKit.
/// </summary>
/// <remarks>
/// Si el servidor no recibe el mensaje lanza <see cref="EmailDeliveryException"/>, con un texto sin la contraseña ni
/// las direcciones de correo (datos personales), porque ese texto llega a los logs y a <c>outbox_messages</c>.
/// </remarks>
public interface IEmailSender
{
    Task SendAsync(MailServer server, EmailMessage message, CancellationToken ct);
}

/// <summary>Cifrado de la conexión con el servidor de correo.</summary>
public enum MailEncryption
{
    /// <summary>Sin cifrado, por ejemplo un servidor de pruebas local.</summary>
    None,

    /// <summary>La conexión empieza sin cifrar y pasa a TLS con STARTTLS (lo que ApiDian llama <c>tls</c>).</summary>
    StartTls,

    /// <summary>TLS desde el primer byte (lo que ApiDian llama <c>ssl</c>, por lo general el puerto 465).</summary>
    SslOnConnect,

    /// <summary>Lo decide la librería según el puerto y lo que ofrezca el servidor.</summary>
    Auto,
}

/// <summary>
/// Servidor de correo con que sale un mensaje y su remitente. La contraseña viaja en claro solo en memoria, mientras
/// dura el envío: nunca se registra ni se guarda, y <see cref="ToString"/> no la incluye.
/// </summary>
/// <param name="Username">Usuario SMTP; nulo si el servidor no pide autenticación.</param>
/// <param name="Password">Contraseña SMTP en claro; nula si el servidor no pide autenticación.</param>
/// <param name="FromAddress">Dirección del remitente.</param>
/// <param name="FromName">Nombre del remitente; nulo para enviar solo la dirección.</param>
public sealed record MailServer(
    string Host, int Port, string? Username, string? Password, MailEncryption Encryption, string FromAddress, string? FromName)
{
    /// <summary>Texto para diagnóstico, sin la contraseña ni el usuario.</summary>
    public override string ToString() => $"MailServer {{ Host = {Host}, Port = {Port}, Encryption = {Encryption} }}";
}

/// <summary>Un correo listo para enviar.</summary>
/// <param name="To">Destinatarios principales.</param>
/// <param name="Bcc">Copias ocultas: ningún destinatario ve las direcciones de los demás.</param>
/// <param name="HtmlBody">Cuerpo en HTML.</param>
/// <param name="TextBody">El mismo cuerpo en texto plano, para los lectores que no muestran HTML.</param>
public sealed record EmailMessage(
    IReadOnlyList<string> To,
    IReadOnlyList<string> Bcc,
    string Subject,
    string HtmlBody,
    string TextBody,
    IReadOnlyList<EmailAttachment> Attachments);

/// <summary>Archivo adjunto de un correo.</summary>
public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

/// <summary>
/// El servidor de correo no recibió el mensaje (no respondió, rechazó la autenticación o algún destinatario). Es una
/// falla transitoria: el Worker reintenta el mensaje con retroceso. Su texto nunca trae la contraseña ni direcciones.
/// </summary>
public sealed class EmailDeliveryException(string message) : Exception(message);
