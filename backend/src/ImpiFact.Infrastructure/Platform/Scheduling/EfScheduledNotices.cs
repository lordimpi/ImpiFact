using System.Security.Cryptography;
using System.Text;
using ImpiFact.Application.Abstractions.Maintenance;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Infrastructure.Platform.Scheduling;

/// <summary>
/// Id fijo de la notificación de un aviso programado: el mismo para la misma llave, en todas las instancias del
/// Worker. Son los primeros 16 bytes del SHA-256 de la llave, marcados como uuid versión 8 (la versión de RFC 9562
/// para uuid armados por la aplicación).
/// </summary>
internal static class ScheduledNoticeIds
{
    public static Guid For(string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("impifact:scheduled-notice:" + key)).AsSpan(0, 16).ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}

/// <summary>
/// Avisos programados sobre EF Core (Entity Framework). El id de la notificación sale de la llave del aviso
/// (<see cref="ScheduledNoticeIds"/>), así la llave primaria de <c>notifications</c> impide el duplicado aunque dos
/// instancias guarden el mismo aviso a la vez: la segunda recibe la violación de la llave y no guarda nada, ni la
/// notificación ni el mensaje de su correo, que van en la misma transacción.
/// </summary>
internal sealed class EfScheduledNotices(ImpiFactDbContext db, IOutbox outbox, IDatabaseProvider provider) : IScheduledNotices
{
    public async Task<bool> TryAddAsync(ScheduledNotice notice, CancellationToken ct)
    {
        var id = ScheduledNoticeIds.For(notice.Key);
        try
        {
            if (await db.Set<Notification>().IgnoreQueryFilters([QueryFilters.Tenant]).AnyAsync(row => row.Id == id, ct))
            {
                return false;
            }

            db.Set<Notification>().Add(new Notification
            {
                Id = id,
                CompanyId = notice.CompanyId,
                Type = notice.Type,
                Title = notice.Title,
                Body = notice.Body,
            });
            if (notice.SendEmail)
            {
                await outbox.EnqueueAsync(
                    OutboxMessageTypes.SendNoticeEmail, new SendNoticeEmailPayload(id), notice.CompanyId, notBefore: null, ct);
            }

            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException exception) when (provider.IsUniqueViolation(exception))
        {
            return false;
        }
        finally
        {
            // El vigilante guarda muchos avisos con el mismo contexto: ninguno queda pendiente para el siguiente.
            db.ChangeTracker.Clear();
        }
    }
}

/// <summary>
/// Lecturas de los vigilantes: consultas sin rastreo que cruzan empresas a propósito (quitan el filtro de empresa),
/// porque las corre el sistema para todas las empresas.
/// </summary>
internal sealed class EfExpiryWatchReads(ImpiFactDbContext db) : IExpiryWatchReads
{
    public async Task<IReadOnlyList<WatchedCertificate>> ListExpiringCertificatesAsync(DateTimeOffset now, DateTimeOffset until, CancellationToken ct) =>
        await db.Set<Certificate>().IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .Where(certificate => certificate.IsActive && certificate.NotAfter > now && certificate.NotAfter <= until)
            .OrderBy(certificate => certificate.NotAfter)
            .Select(certificate => new WatchedCertificate(certificate.CompanyId, certificate.Id, certificate.Name, certificate.NotAfter))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<WatchedResolution>> ListActiveResolutionsAsync(CancellationToken ct)
    {
        var resolutions = await db.Set<Resolution>().IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .Where(resolution => resolution.IsActive)
            .OrderBy(resolution => resolution.CompanyId).ThenBy(resolution => resolution.Id)
            .ToListAsync(ct);
        var keys = resolutions.Select(resolution => NumberingScope.ForResolution(resolution.Id)).ToList();
        var sequences = await db.Set<NumberingSequence>().IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .Where(sequence => keys.Contains(sequence.ScopeKey))
            .Select(sequence => new { sequence.CompanyId, sequence.ScopeKey, sequence.NextNumber })
            .ToListAsync(ct);
        var next = sequences.ToDictionary(sequence => (sequence.CompanyId, sequence.ScopeKey), sequence => sequence.NextNumber);

        return resolutions
            .Select(resolution => new WatchedResolution(
                resolution.CompanyId,
                resolution.Id,
                resolution.DocumentTypeId,
                resolution.Prefix,
                resolution.ResolutionNumber,
                resolution.RangeFrom,
                resolution.RangeTo,
                next.TryGetValue((resolution.CompanyId, NumberingScope.ForResolution(resolution.Id)), out var number) ? number : null,
                resolution.ValidTo))
            .ToList();
    }
}

/// <summary>El aviso y los datos de su empresa para el correo, con una consulta sin rastreo por empresa.</summary>
internal sealed class EfCompanyNoticeReads(ImpiFactDbContext db) : ICompanyNoticeReads
{
    public async Task<CompanyNotice?> FindAsync(Guid companyId, Guid notificationId, CancellationToken ct)
    {
        var notice = await db.Set<Notification>().IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .Where(row => row.Id == notificationId && row.CompanyId == companyId)
            .Select(row => new { row.Title, row.Body })
            .SingleOrDefaultAsync(ct);
        var company = notice is null
            ? null
            : await db.Set<Company>().AsNoTracking()
                .Where(row => row.Id == companyId)
                .Select(row => new { row.Name, row.Email })
                .SingleOrDefaultAsync(ct);
        return notice is null || company is null ? null : new CompanyNotice(company.Name, company.Email, notice.Title, notice.Body);
    }
}
