using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Identity;
using ImpiFact.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>
/// Adaptador de <see cref="IApiKeyReads"/> con EF Core (Entity Framework) sin rastreo. <c>api_keys</c> no tiene filtro
/// global de empresa (también guarda las llaves de plataforma), así que la empresa va siempre en el WHERE: una llave
/// de empresa se busca por su <c>company_id</c> y una de plataforma por <c>company_id</c> nulo. La proyección nunca
/// lee <c>key_hash</c>.
/// </summary>
internal sealed class EfApiKeyReads(ImpiFactDbContext db) : IApiKeyReads
{
    private const char ScopeSeparator = ',';

    public async Task<IReadOnlyList<ApiKeySummary>> ListAsync(ApiKeyListFilter filter, CancellationToken ct)
    {
        var query = db.Set<ApiKey>().AsNoTracking()
            .Where(key => filter.CompanyId == null ? key.CompanyId == null : key.CompanyId == filter.CompanyId);
        if (filter.After is { } after)
        {
            // Siguiente página por clave (created_at, id), de la más nueva a la más antigua.
            var createdAt = after.CreatedAt.ToUniversalTime();
            query = query.Where(key => key.CreatedAt < createdAt || (key.CreatedAt == createdAt && key.Id.CompareTo(after.Id) < 0));
        }

        var rows = await query
            .OrderByDescending(key => key.CreatedAt)
            .ThenByDescending(key => key.Id)
            .Take(filter.Take)
            .Select(key => new { key.Id, key.Name, key.KeyPrefix, key.Scopes, key.CreatedAt, key.ExpiresAt, key.LastUsedAt, key.RevokedAt })
            .ToListAsync(ct);
        return rows
            .Select(key => new ApiKeySummary(
                key.Id,
                key.Name,
                key.KeyPrefix,
                [.. key.Scopes.Split(ScopeSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
                key.CreatedAt,
                key.ExpiresAt,
                key.LastUsedAt,
                key.RevokedAt))
            .ToList();
    }
}
