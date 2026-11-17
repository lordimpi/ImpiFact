using System.Globalization;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Maintenance;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Features.Maintenance;

/// <summary>
/// Tarea diaria <c>CertificateExpiryWatcher</c> (Arquitectura detallada, sección 8): deja una notificación y un correo
/// a la empresa cuando a su certificado de firma activo le faltan 30, 15 o 5 días para vencer, contados en el
/// calendario de Colombia.
/// </summary>
/// <remarks>
/// Cada umbral se avisa una sola vez por certificado: la llave del aviso es el certificado y el umbral, y
/// <see cref="IScheduledNotices"/> no guarda dos veces la misma llave. Si el Worker no corrió el día exacto (estuvo
/// apagado), se avisa el umbral más estrecho alcanzado y los más amplios se dan por cubiertos; por ejemplo, un
/// certificado a 12 días que nunca recibió aviso recibe solo el de 15. La falla de un certificado se registra y no
/// detiene a los demás.
/// </remarks>
internal sealed class CertificateExpiryWatcher(
    IExpiryWatchReads reads, IScheduledNotices notices, IClock clock, ILogger<CertificateExpiryWatcher> logger) : IScheduledJob
{
    /// <summary>Días antes del vencimiento en que se avisa, del más amplio al más estrecho.</summary>
    internal static readonly int[] Thresholds = [30, 15, 5];

    internal const string NoticeType = "CertificateExpiring";

    public string Name => "CertificateExpiryWatcher";

    public async Task RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;

        // Un día de más en la consulta: los días se cuentan en el calendario de Colombia, no en horas.
        var certificates = await reads.ListExpiringCertificatesAsync(now, now.AddDays(Thresholds[0] + 2), ct);
        var created = 0;
        foreach (var certificate in certificates)
        {
            var daysLeft = ColombiaCalendar.DaysBetween(now, certificate.NotAfter);
            if (daysLeft > Thresholds[0])
            {
                continue;
            }

            var threshold = Thresholds.Last(candidate => daysLeft <= candidate);
            try
            {
                if (await notices.TryAddAsync(NoticeFor(certificate, daysLeft, threshold), ct))
                {
                    created++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(
                    exception, "Could not record the expiry notice of certificate {CertificateId} of company {CompanyId}.",
                    certificate.CertificateId, certificate.CompanyId);
            }
        }

        logger.LogInformation(
            "Certificate expiry watcher checked {CertificateCount} certificates and created {NoticeCount} notices.", certificates.Count, created);
    }

    private static ScheduledNotice NoticeFor(WatchedCertificate certificate, int daysLeft, int threshold)
    {
        var when = daysLeft switch
        {
            0 => "hoy",
            1 => "mañana",
            _ => string.Create(CultureInfo.InvariantCulture, $"en {daysLeft} días"),
        };
        var date = ColombiaCalendar.DateOf(certificate.NotAfter).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        return new ScheduledNotice(
            certificate.CompanyId,
            string.Create(CultureInfo.InvariantCulture, $"certificate-expiry:{certificate.CertificateId:D}:{threshold}"),
            NoticeType,
            $"El certificado de firma vence {when}",
            $"El certificado de firma «{certificate.Name}» vence el {date} (fecha de Colombia). Después de esa fecha no se "
            + "pueden firmar ni enviar documentos a la DIAN. Suba el certificado nuevo desde el panel antes de que venza.",
            SendEmail: true);
    }
}
