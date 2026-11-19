using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Identity;
using ImpiFact.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>
/// Adaptador de <see cref="IApiKeyStore"/> sobre la tabla <c>api_keys</c> con EF Core (Entity Framework, el
/// mapeador de objetos a tablas). Los alcances se guardan separados por coma en <c>scopes</c>. Nunca guarda ni lee
/// el texto claro de una llave: solo su resumen SHA-256 (<c>key_hash</c>).
/// </summary>
internal sealed class EfApiKeyStore(ImpiFactDbContext db, CredentialStoreGuard guard) : IApiKeyStore
{
    private const char ScopeSeparator = ',';

    public Task<ApiKeyRecord?> FindByHashAsync(string keyHash, CancellationToken ct) =>
        guard.RunAsync(async () => ToRecord(
            await db.Set<ApiKey>().AsNoTracking().FirstOrDefaultAsync(key => key.KeyHash == keyHash, ct)));

    public Task<ApiKeyRecord?> FindByIdAsync(Guid apiKeyId, CancellationToken ct) =>
        guard.RunAsync(async () => ToRecord(
            await db.Set<ApiKey>().AsNoTracking().FirstOrDefaultAsync(key => key.Id == apiKeyId, ct)));

    /// <summary>
    /// Se llama desde la cola en segundo plano, con un ámbito de servicios propio: guarda aquí mismo. Solo cambia
    /// <c>last_used_at</c> (y <c>updated_at</c>, que el interceptor de auditoría mueve en todo guardado) y nunca lo
    /// mueve hacia atrás.
    /// </summary>
    public Task MarkUsedAsync(Guid apiKeyId, DateTimeOffset usedAt, CancellationToken ct) =>
        guard.RunAsync(async () =>
        {
            var key = await db.Set<ApiKey>().FirstOrDefaultAsync(candidate => candidate.Id == apiKeyId, ct);
            if (key is null || key.LastUsedAt >= usedAt)
            {
                return;
            }

            key.LastUsedAt = usedAt;
            await db.SaveChangesAsync(ct);
        });

    public Task AddAsync(NewApiKey key, CancellationToken ct)
    {
        db.Add(new ApiKey
        {
            Id = key.Id,
            CompanyId = key.CompanyId,
            Name = key.Name,
            KeyPrefix = key.KeyPrefix,
            KeyHash = key.KeyHash,
            Scopes = string.Join(ScopeSeparator, key.Scopes),
            ExpiresAt = key.ExpiresAt,
            CreatedBy = key.CreatedBy,
        });
        return Task.CompletedTask;
    }

    public Task RevokeAsync(Guid apiKeyId, DateTimeOffset revokedAt, CancellationToken ct) =>
        guard.RunAsync(async () =>
        {
            var key = await db.Set<ApiKey>().FirstOrDefaultAsync(candidate => candidate.Id == apiKeyId, ct);
            if (key is not null && key.RevokedAt is null)
            {
                key.RevokedAt = revokedAt;
            }
        });

    private static ApiKeyRecord? ToRecord(ApiKey? key) => key is null
        ? null
        : new ApiKeyRecord(
            key.Id,
            key.CompanyId,
            key.KeyHash,
            [.. key.Scopes.Split(ScopeSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            key.ExpiresAt,
            key.RevokedAt);
}
