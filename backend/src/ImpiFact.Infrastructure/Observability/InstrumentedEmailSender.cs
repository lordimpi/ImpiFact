using ImpiFact.Application.Abstractions.Email;
using ImpiFact.Application.Abstractions.Observability;

namespace ImpiFact.Infrastructure.Observability;

/// <summary>
/// Cuenta cada correo que se intenta enviar (<see cref="IEmailSender"/>): <c>impifact.emails.sent</c> si el servidor lo
/// aceptó y <c>impifact.emails.failed</c> si falló. Una cancelación (el Worker se apaga) no cuenta. Sin etiquetas: ni
/// destinatarios ni servidor.
/// </summary>
internal sealed class InstrumentedEmailSender(IEmailSender inner, ImpiFactMetrics metrics) : IEmailSender
{
    public async Task SendAsync(MailServer server, EmailMessage message, CancellationToken ct)
    {
        try
        {
            await inner.SendAsync(server, message, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            metrics.EmailFailed();
            throw;
        }

        metrics.EmailSent();
    }
}
