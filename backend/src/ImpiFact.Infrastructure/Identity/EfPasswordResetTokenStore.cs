using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Identity;
using ImpiFact.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>
/// Adaptador de <see cref="IPasswordResetTokenStore"/> sobre la tabla <c>password_reset_tokens</c> con EF Core (Entity
/// Framework). Solo guarda el resumen SHA-256 de cada token (<c>token_hash</c>), nunca el valor.
/// </summary>
internal sealed class EfPasswordResetTokenStore(ImpiFactDbContext db) : IPasswordResetTokenStore
{
    public Task AddAsync(PasswordResetTokenRecord token, CancellationToken ct)
    {
        db.Add(new PasswordResetToken
        {
            Id = token.Id,
            UserId = token.UserId,
            TokenHash = token.TokenHash,
            ExpiresAt = token.ExpiresAt,
            UsedAt = token.UsedAt,
            RevokedAt = token.RevokedAt,
            RequestedIp = token.RequestedIp is { Length: > EfRefreshTokenStore.CreatedIpLength } ip
                ? ip[..EfRefreshTokenStore.CreatedIpLength]
                : token.RequestedIp,
        });
        return Task.CompletedTask;
    }

    public Task RevokeUnusedForUserAsync(Guid userId, DateTimeOffset revokedAt, CancellationToken ct) =>
        CredentialStoreGuard.RunAsync(async () =>
        {
            var unused = await db.Set<PasswordResetToken>()
                .Where(token => token.UserId == userId && token.UsedAt == null && token.RevokedAt == null)
                .ToListAsync(ct);
            foreach (var token in unused)
            {
                token.RevokedAt = revokedAt;
            }
        });

    public Task<PasswordResetTokenRecord?> FindByHashAsync(string tokenHash, CancellationToken ct) =>
        CredentialStoreGuard.RunAsync(() => db.Set<PasswordResetToken>().AsNoTracking()
            .Where(token => token.TokenHash == tokenHash)
            .Select(token => new PasswordResetTokenRecord(
                token.Id, token.UserId, token.TokenHash, token.ExpiresAt, token.UsedAt, token.RevokedAt, token.RequestedIp))
            .FirstOrDefaultAsync(ct));

    // Se lee la fila con seguimiento: si otra solicitud ya la usó y confirmó, aquí se ve y se devuelve false. Si las dos
    // la leen a la vez, row_version hace fallar el segundo guardado (conflicto de concurrencia) y su transacción se revierte.
    public Task<bool> MarkUsedAsync(Guid tokenId, DateTimeOffset usedAt, CancellationToken ct) =>
        CredentialStoreGuard.RunAsync(async () =>
        {
            var token = await db.Set<PasswordResetToken>().FirstOrDefaultAsync(candidate => candidate.Id == tokenId, ct);
            if (token is null || token.UsedAt is not null || token.RevokedAt is not null)
            {
                return false;
            }

            token.UsedAt = usedAt;
            return true;
        });

    public Task<PasswordResetRecipient?> FindRecipientAsync(Guid tokenId, CancellationToken ct) =>
        CredentialStoreGuard.RunAsync(() => db.Set<PasswordResetToken>().AsNoTracking()
            .Where(token => token.Id == tokenId)
            .Join(db.Set<User>(), token => token.UserId, user => user.Id, (token, user) => new PasswordResetRecipient(
                new PasswordResetTokenRecord(token.Id, token.UserId, token.TokenHash, token.ExpiresAt, token.UsedAt, token.RevokedAt, token.RequestedIp),
                user.Email,
                user.Name,
                user.IsActive))
            .FirstOrDefaultAsync(ct));
}
