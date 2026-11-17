using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Maintenance;

namespace ImpiFact.Application.Tests.Features.Maintenance;

/// <summary>Reloj fijo de las pruebas de mantenimiento.</summary>
public sealed class MaintenanceClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

/// <summary>Lecturas en memoria; anota con qué rango de fechas se pidieron los certificados.</summary>
public sealed class FakeExpiryWatchReads : IExpiryWatchReads
{
    public List<WatchedCertificate> Certificates { get; } = [];

    public List<WatchedResolution> Resolutions { get; } = [];

    public (DateTimeOffset Now, DateTimeOffset Until)? CertificateWindow { get; private set; }

    public Task<IReadOnlyList<WatchedCertificate>> ListExpiringCertificatesAsync(DateTimeOffset now, DateTimeOffset until, CancellationToken ct)
    {
        CertificateWindow = (now, until);
        return Task.FromResult<IReadOnlyList<WatchedCertificate>>(
            Certificates.Where(certificate => certificate.NotAfter > now && certificate.NotAfter <= until).ToList());
    }

    public Task<IReadOnlyList<WatchedResolution>> ListActiveResolutionsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<WatchedResolution>>(Resolutions);
}

/// <summary>Avisos en memoria que, como el real, guardan una sola vez cada llave.</summary>
public sealed class FakeScheduledNotices : IScheduledNotices
{
    public List<ScheduledNotice> Saved { get; } = [];

    public int Attempts { get; private set; }

    public Task<bool> TryAddAsync(ScheduledNotice notice, CancellationToken ct)
    {
        Attempts++;
        if (Saved.Any(saved => saved.Key == notice.Key))
        {
            return Task.FromResult(false);
        }

        Saved.Add(notice);
        return Task.FromResult(true);
    }
}
