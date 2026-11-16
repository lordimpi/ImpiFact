using ImpiFact.Application.Abstractions.Identity;
using Microsoft.Extensions.Caching.Hybrid;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>
/// Caché de la búsqueda de API keys por resumen (Arquitectura detallada, sección 19.1): HybridCache (la caché de
/// .NET que evita que varias solicitudes simultáneas consulten la base por la misma llave) solo en la memoria de
/// la instancia, por 60 segundos. Una llave revocada deja de servir como máximo en ese tiempo, y de inmediato en la
/// instancia que ejecuta la revocación, que llama a <see cref="InvalidateAsync"/>.
/// </summary>
internal sealed class ApiKeyCache(HybridCache cache) : IApiKeyCacheInvalidator
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    // Solo memoria local: la revocación limpia la entrada de esta instancia, como pide la arquitectura. Si algún día
    // se registra una caché distribuida, esta búsqueda no la usa.
    private static readonly HybridCacheEntryOptions EntryOptions = new()
    {
        Expiration = Lifetime,
        LocalCacheExpiration = Lifetime,
        Flags = HybridCacheEntryFlags.DisableDistributedCache,
    };

    public async Task<ApiKeyRecord?> GetOrLoadAsync(string keyHash, IApiKeyStore store, CancellationToken ct)
    {
        var key = await cache.GetOrCreateAsync(
            CacheKey(keyHash),
            (store, keyHash),
            static (state, token) => new ValueTask<ApiKeyRecord?>(state.store.FindByHashAsync(state.keyHash, token)),
            EntryOptions,
            cancellationToken: ct);

        // Las llaves que no existen no se guardan: así, alguien que prueba llaves al azar no llena la memoria.
        if (key is null)
        {
            await cache.RemoveAsync(CacheKey(keyHash), ct);
        }

        return key;
    }

    public Task InvalidateAsync(string keyHash, CancellationToken ct) => cache.RemoveAsync(CacheKey(keyHash), ct).AsTask();

    private static string CacheKey(string keyHash) => $"api-key:{keyHash}";
}
