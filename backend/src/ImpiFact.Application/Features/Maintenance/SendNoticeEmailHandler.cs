using System.Net;
using ImpiFact.Application.Abstractions.Email;
using ImpiFact.Application.Abstractions.Maintenance;
using ImpiFact.Application.Abstractions.Messaging;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Features.Maintenance;

/// <summary>
/// Handler del mensaje <c>SendNoticeEmail</c> en el Worker: envía al correo de la empresa un aviso de las tareas
/// programadas (por ejemplo, el certificado de firma por vencer), con el mismo título y texto de la notificación del
/// panel.
/// </summary>
/// <remarks>
/// <para>
/// Sale con el servidor de correo de la plataforma (sección «Email»), no con el de la empresa: es un aviso de ImpiFact
/// a su cliente, no un correo de la empresa a sus compradores. Por eso no usa el selector de servidor del correo de
/// documentos.
/// </para>
/// <para>
/// El mensaje se encola en la misma transacción que la notificación, que se guarda una sola vez por llave, así el
/// correo también sale una sola vez. Como en todo handler, la entrega es «al menos una vez»: si el servidor recibió
/// el correo y el Worker se cae antes de marcar el mensaje hecho, se envía otra vez. Los logs nunca llevan la
/// dirección de correo.
/// </para>
/// </remarks>
internal sealed class SendNoticeEmailHandler(
    ICompanyNoticeReads notices, IPlatformMailServer platform, IEmailSender sender, ILogger<SendNoticeEmailHandler> logger)
    : IOutboxMessageHandler
{
    public string MessageType => OutboxMessageTypes.SendNoticeEmail;

    public async Task HandleAsync(OutboxMessageContext message, CancellationToken ct)
    {
        var payload = message.GetPayload<SendNoticeEmailPayload>();
        var companyId = message.CompanyId
            ?? throw new PermanentOutboxFailureException($"Outbox message {message.MessageId} has no company.");
        var notice = await notices.FindAsync(companyId, payload.NotificationId, ct)
            ?? throw new PermanentOutboxFailureException($"Notification {payload.NotificationId} of company {companyId} does not exist.");
        if (string.IsNullOrWhiteSpace(notice.CompanyEmail))
        {
            logger.LogInformation("Company {CompanyId} has no email; notification {NotificationId} is not emailed.", companyId, payload.NotificationId);
            return;
        }

        const string footer = "Este aviso también está en las notificaciones del panel de ImpiFact.";
        var greeting = $"Hola, {notice.CompanyName}:";
        var text = $"{greeting}\n\n{notice.Body}\n\n{footer}";
        var html = $"<p>{WebUtility.HtmlEncode(greeting)}</p><p>{WebUtility.HtmlEncode(notice.Body)}</p><p>{WebUtility.HtmlEncode(footer)}</p>";
        var email = new EmailMessage([notice.CompanyEmail.Trim()], [], $"{notice.CompanyName}: {notice.Title}", html, text, []);

        await sender.SendAsync(platform.Default, email, ct);
        logger.LogInformation("Notification {NotificationId} of company {CompanyId} was emailed.", payload.NotificationId, companyId);
    }
}
