using System.Globalization;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Maintenance;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Features.Maintenance;

/// <summary>
/// Tarea diaria <c>ResolutionRangeWatcher</c> (Arquitectura detallada, sección 8): deja una notificación a la empresa
/// cuando a una resolución de numeración activa le queda menos del 10 % de su rango o le faltan menos de 30 días
/// para <c>valid_to</c> (fin de su vigencia), contados en el calendario de Colombia. No envía correo. Las dos
/// condiciones son las de <see cref="ResolutionAlertRule"/>, las mismas que muestra el tablero del panel.
/// </summary>
/// <remarks>
/// Cada condición se avisa una sola vez por resolución. La llave del aviso lleva el fin del rango o la fecha de
/// vigencia: si la empresa actualiza la resolución con un rango o una vigencia nuevos y vuelven a quedar pocos
/// números o días, se avisa otra vez. La falla de una resolución se registra y no detiene a las demás.
/// </remarks>
internal sealed class ResolutionRangeWatcher(
    IExpiryWatchReads reads, IScheduledNotices notices, IClock clock, ILogger<ResolutionRangeWatcher> logger) : IScheduledJob
{
    internal const string NumbersLowType = "ResolutionNumbersLow";
    internal const string ExpiringType = "ResolutionExpiring";

    public string Name => "ResolutionRangeWatcher";

    public async Task RunAsync(CancellationToken ct)
    {
        var today = ColombiaCalendar.DateOf(clock.UtcNow);
        var resolutions = await reads.ListActiveResolutionsAsync(ct);
        var created = 0;
        foreach (var resolution in resolutions)
        {
            try
            {
                foreach (var notice in NoticesFor(resolution, today))
                {
                    if (await notices.TryAddAsync(notice, ct))
                    {
                        created++;
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(
                    exception, "Could not record the notices of resolution {ResolutionId} of company {CompanyId}.",
                    resolution.ResolutionId, resolution.CompanyId);
            }
        }

        logger.LogInformation(
            "Resolution range watcher checked {ResolutionCount} resolutions and created {NoticeCount} notices.", resolutions.Count, created);
    }

    private static IEnumerable<ScheduledNotice> NoticesFor(WatchedResolution resolution, DateOnly today)
    {
        var label = Label(resolution);
        var alert = ResolutionAlertRule.Evaluate(resolution.RangeFrom, resolution.RangeTo, resolution.NextNumber, resolution.ValidTo, today);
        var (total, remaining) = (alert.Total, alert.Remaining);
        if (alert.NumbersLow)
        {
            yield return new ScheduledNotice(
                resolution.CompanyId,
                Invariant($"resolution-range:{resolution.ResolutionId:D}:numbers:{resolution.RangeTo}"),
                NumbersLowType,
                remaining == 0 ? $"Se agotaron los números de la resolución {label}" : $"Quedan pocos números en la resolución {label}",
                Invariant($"Quedan {remaining} de los {total} números autorizados (del {resolution.RangeFrom} al {resolution.RangeTo}). ")
                + "Solicite a la DIAN una nueva resolución de numeración y regístrela antes de que se agoten.",
                SendEmail: false);
        }

        if (resolution.ValidTo is { } validTo && alert.Expiring)
        {
            var expired = alert.Expired;
            var date = validTo.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            yield return new ScheduledNotice(
                resolution.CompanyId,
                Invariant($"resolution-validity:{resolution.ResolutionId:D}:{validTo:yyyy-MM-dd}"),
                ExpiringType,
                expired ? $"La resolución {label} está vencida" : $"La resolución {label} vence pronto",
                $"La vigencia de la resolución {label} {(expired ? "terminó" : "termina")} el {date}. Solicite a la DIAN una "
                + $"nueva resolución de numeración y regístrela {(expired ? "para seguir emitiendo" : "antes de esa fecha")}.",
                SendEmail: false);
        }
    }

    /// <summary>Cómo se nombra la resolución en el aviso: su número y su prefijo, si los tiene.</summary>
    private static string Label(WatchedResolution resolution) =>
        (string.IsNullOrWhiteSpace(resolution.ResolutionNumber), string.IsNullOrWhiteSpace(resolution.Prefix)) switch
        {
            (false, false) => $"{resolution.ResolutionNumber} con prefijo {resolution.Prefix}",
            (false, true) => resolution.ResolutionNumber!,
            (true, false) => $"con prefijo {resolution.Prefix}",
            _ => "de numeración",
        };

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
