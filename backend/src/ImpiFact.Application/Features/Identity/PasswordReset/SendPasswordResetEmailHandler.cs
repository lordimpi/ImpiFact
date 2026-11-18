using System.Net;
using System.Security.Cryptography;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Email;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Abstractions.Protection;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Features.Identity.PasswordReset;

/// <summary>
/// Handler del mensaje <c>SendPasswordResetEmail</c> en el Worker: envía al usuario el enlace del panel para elegir la
/// contraseña nueva, con el servidor de correo de la plataforma (sección «Email»), porque es un correo de ImpiFact y no
/// de una empresa.
/// </summary>
/// <remarks>
/// <para>
/// El correo y el nombre se leen de la base al enviar, así no viajan en la bandeja de salida; el token llega cifrado y se
/// descifra solo para armar el enlace. Ni el token ni el enlace ni la dirección de correo van a los registros.
/// </para>
/// <para>
/// Si el token ya no sirve (se usó, se pidió otro, venció o el usuario quedó inactivo), no se envía: el enlace no
/// funcionaría. Como todo handler, la entrega es «al menos una vez»: si el Worker se cae después de enviar y antes de
/// marcar el mensaje hecho, el mismo enlace puede llegar dos veces.
/// </para>
/// </remarks>
internal sealed class SendPasswordResetEmailHandler(
    IPasswordResetTokenStore resetTokens,
    ISecretProtector secrets,
    IPanelLinks links,
    IPlatformMailServer platform,
    IEmailSender sender,
    IClock clock,
    ILogger<SendPasswordResetEmailHandler> logger) : IOutboxMessageHandler
{
    public string MessageType => OutboxMessageTypes.SendPasswordResetEmail;

    public async Task HandleAsync(OutboxMessageContext message, CancellationToken ct)
    {
        var payload = message.GetPayload<SendPasswordResetEmailPayload>();
        var recipient = await resetTokens.FindRecipientAsync(payload.ResetTokenId, ct);
        if (recipient is null || !recipient.UserIsActive || !recipient.Token.IsUsableAt(clock.UtcNow))
        {
            logger.LogInformation("Reset token {ResetTokenId} is no longer usable; its email is not sent.", payload.ResetTokenId);
            return;
        }

        string token;
        try
        {
            token = secrets.Unprotect(PasswordResetEmail.SecretPurpose, payload.TokenCipher);
        }
        catch (CryptographicException exception)
        {
            // Reintentar no lo arregla: el texto cifrado está alterado o sus llaves ya no existen.
            throw new PermanentOutboxFailureException($"The token of reset token {payload.ResetTokenId} cannot be decrypted.", exception);
        }

        var link = links.PasswordReset(token).AbsoluteUri;
        await sender.SendAsync(platform.Default, Compose(recipient, link), ct);
        logger.LogInformation("The email of reset token {ResetTokenId} was sent.", payload.ResetTokenId);
    }

    /// <summary>El correo en texto plano y en HTML, con los mismos párrafos; el HTML escapa el nombre y el enlace.</summary>
    internal static EmailMessage Compose(PasswordResetRecipient recipient, string link)
    {
        string[] paragraphs =
        [
            $"Hola, {recipient.Name}:",
            "Recibimos una solicitud para restablecer la contraseña de su cuenta en ImpiFact. Para elegir una contraseña nueva, abra este enlace:",
            link,
            $"El enlace vence en {PasswordResetEmail.LifetimeText} y solo se puede usar una vez. Al cambiar la contraseña se cerrarán todas sus sesiones abiertas.",
            "Si usted no pidió este cambio, ignore este correo: su contraseña sigue igual.",
        ];
        var text = string.Join("\n\n", paragraphs);
        var html = string.Concat(paragraphs.Select(paragraph => paragraph == link
            ? $"<p><a href=\"{WebUtility.HtmlEncode(link)}\">{WebUtility.HtmlEncode(link)}</a></p>"
            : $"<p>{WebUtility.HtmlEncode(paragraph)}</p>"));
        return new EmailMessage([recipient.Email.Trim()], [], "Restablezca su contraseña de ImpiFact", html, text, []);
    }
}
