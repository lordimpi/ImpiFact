using ImpiFact.Application.Abstractions.Email;
using ImpiFact.Infrastructure.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ImpiFact.Infrastructure.Email;

/// <summary>
/// Envía correos por SMTP (el protocolo de envío de correo) con MailKit (librería MIT): abre una conexión por mensaje
/// con el servidor recibido, se autentica si trae usuario y la cierra al terminar.
/// </summary>
/// <remarks>
/// Ninguna falla deja ver la contraseña ni las direcciones de correo (datos personales): se cambia por
/// <see cref="EmailDeliveryException"/> con el servidor, el tipo de falla y, si lo hay, el código SMTP, sin el texto
/// del servidor (que puede repetir una dirección). Nunca se activa el registro del protocolo de MailKit.
/// </remarks>
internal sealed class MailKitEmailSender(IOptions<EmailOptions> options, ILogger<MailKitEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(MailServer server, EmailMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(message);
        using var mime = Build(server, message);
        using var client = new SmtpClient { Timeout = options.Value.TimeoutSeconds * 1000 };
        try
        {
            await client.ConnectAsync(server.Host, server.Port, SocketOptionsOf(server.Encryption), ct);
            if (!string.IsNullOrEmpty(server.Username))
            {
                await client.AuthenticateAsync(server.Username, server.Password ?? string.Empty, ct);
            }

            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(quit: true, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var reason = Describe(server, exception);
            logger.LogWarning("Email delivery failed: {Reason}", reason);
            throw new EmailDeliveryException(reason);
        }
    }

    private static MimeMessage Build(MailServer server, EmailMessage message)
    {
        var mime = new MimeMessage { Subject = message.Subject };
        mime.From.Add(new MailboxAddress(server.FromName ?? string.Empty, server.FromAddress));
        mime.To.AddRange(message.To.Select(address => MailboxAddress.Parse(address)));
        mime.Bcc.AddRange(message.Bcc.Select(address => MailboxAddress.Parse(address)));
        var body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody };
        foreach (var attachment in message.Attachments)
        {
            body.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
        }

        mime.Body = body.ToMessageBody();
        return mime;
    }

    private static SecureSocketOptions SocketOptionsOf(MailEncryption encryption) => encryption switch
    {
        MailEncryption.None => SecureSocketOptions.None,
        MailEncryption.StartTls => SecureSocketOptions.StartTls,
        MailEncryption.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.Auto,
    };

    private static string Describe(MailServer server, Exception exception)
    {
        var smtp = exception is SmtpCommandException command ? $", SMTP status {(int)command.StatusCode} ({command.ErrorCode})" : string.Empty;
        return $"the mail server {server.Host}:{server.Port} did not take the email ({exception.GetType().Name}{smtp}).";
    }
}
