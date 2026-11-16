using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Identity;
using ImpiFact.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>
/// Adaptador de <see cref="IRefreshTokenStore"/> sobre la tabla <c>refresh_tokens</c> con EF Core (Entity
/// Framework). Solo guarda el resumen SHA-256 de cada token (<c>token_hash</c>), nunca el valor.
/// </summary>
internal sealed class EfRefreshTokenStore(ImpiFactDbContext db, IServiceScopeFactory scopes) : IRefreshTokenStore
{
    /// <summary>Largo de <c>created_ip</c>: el de una dirección IPv6 escrita completa.</summary>
    internal const int CreatedIpLength = 45;

    /// <summary>Largo de <c>user_agent</c>; un navegador puede mandar más y se recorta.</summary>
    internal const int UserAgentLength = 300;

    public Task AddAsync(RefreshTokenRecord token, CancellationToken ct)
    {
        db.Add(new RefreshToken
        {
            Id = token.Id,
            UserId = token.UserId,
            TokenHash = token.TokenHash,
            ExpiresAt = token.ExpiresAt,
            RevokedAt = token.RevokedAt,
            ReplacedBy = token.ReplacedBy,
            CreatedIp = Truncate(token.CreatedIp, CreatedIpLength),
            UserAgent = Truncate(token.UserAgent, UserAgentLength),
        });
        return Task.CompletedTask;
    }

    public Task<RefreshTokenRecord?> FindByHashAsync(string tokenHash, CancellationToken ct) =>
        CredentialStoreGuard.RunAsync(async () =>
        {
            var token = await db.Set<RefreshToken>().AsNoTracking().FirstOrDefaultAsync(candidate => candidate.TokenHash == tokenHash, ct);
            return token is null
                ? null
                : new RefreshTokenRecord(
                    token.Id, token.UserId, token.TokenHash, token.ExpiresAt, token.RevokedAt, token.ReplacedBy, token.CreatedIp, token.UserAgent);
        });

    /// <summary>
    /// Carga la fila con su <c>row_version</c>: si dos solicitudes rotan el mismo token a la vez, la segunda en
    /// guardar falla por concurrencia y no se crean dos cadenas.
    /// </summary>
    // Se lee la fila con seguimiento: si otra solicitud la revocó y confirmó antes, aquí se ve y se devuelve false. Si
    // las dos la leen a la vez, row_version hace fallar el segundo guardado (conflicto de concurrencia).
    public Task<bool> RevokeAsync(Guid tokenId, DateTimeOffset revokedAt, Guid? replacedBy, CancellationToken ct) =>
        CredentialStoreGuard.RunAsync(async () =>
        {
            var token = await db.Set<RefreshToken>().FirstOrDefaultAsync(candidate => candidate.Id == tokenId, ct);
            if (token is null || token.RevokedAt is not null)
            {
                return false;
            }

            token.RevokedAt = revokedAt;
            token.ReplacedBy = replacedBy;
            return true;
        });

    /// <summary>
    /// Usa un ámbito de servicios propio, con su propio contexto y su propia conexión, y guarda de inmediato: la
    /// transacción de la solicitud se va a revertir porque el caso de uso responde con un error.
    /// </summary>
    public Task RevokeAllActiveForUserAsync(Guid userId, DateTimeOffset revokedAt, CancellationToken ct) =>
        CredentialStoreGuard.RunAsync(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var independent = scope.ServiceProvider.GetRequiredService<ImpiFactDbContext>();
            var active = await independent.Set<RefreshToken>()
                .Where(token => token.UserId == userId && token.RevokedAt == null)
                .ToListAsync(ct);
            foreach (var token in active)
            {
                token.RevokedAt = revokedAt;
            }

            await independent.SaveChangesAsync(ct);
        });

    private static string? Truncate(string? value, int length) =>
        value is { Length: var actual } && actual > length ? value[..length] : value;
}
