using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Identity;
using ImpiFact.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>
/// Adaptador de <see cref="IPasswordResetTokenStore"/> sobre la tabla <c>password_reset_tokens</c> con EF Core (Entity
/// Framework). Solo guarda el resumen SHA-256 de cada token (<c>token_hash</c>), nunca el valor.
/// </summary>
internal sealed class EfPasswordResetTokenStore(ImpiFactDbContext db, IClock clock) : IPasswordResetTokenStore
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

    // Un solo UPDATE en lugar de cargar cada token y guardarlo con row_version: con la carga, dos solicitudes simultáneas
    // del mismo usuario chocaban al guardar (409 solo para un correo registrado, lo que revela que la cuenta existe).
    // Con el UPDATE, la segunda espera el bloqueo de las filas, vuelve a evaluar la condición y no cambia nada. Corre en
    // la transacción que abrió el decorador de transacción, así que se revierte con el resto si algo falla. No pasa por
    // los interceptores de guardado, así que pone aquí lo mismo que ellos: mueve updated_at y sube row_version. Subir
    // row_version mantiene el choque con una confirmación que leyó el token antes y lo guarda después.
    public Task RevokeUnusedForUserAsync(Guid userId, DateTimeOffset revokedAt, CancellationToken ct) =>
        CredentialStoreGuard.RunAsync(() =>
        {
            var now = clock.UtcNow;
            return db.Set<PasswordResetToken>()
                .Where(token => token.UserId == userId && token.UsedAt == null && token.RevokedAt == null)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(token => token.RevokedAt, revokedAt)
                        .SetProperty(token => token.UpdatedAt, now)
                        .SetProperty(token => token.RowVersion, token => token.RowVersion + 1),
                    ct);
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
