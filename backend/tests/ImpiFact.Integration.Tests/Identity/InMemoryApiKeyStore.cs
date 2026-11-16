using System.Collections.Concurrent;
using ImpiFact.Application.Abstractions.Identity;

namespace ImpiFact.Integration.Tests.Identity;

/// <summary>
/// Doble de prueba del almacén de API keys: guarda las llaves en memoria, cuenta las búsquedas (para comprobar la
/// caché) y anota cada uso que llega a <see cref="MarkUsedAsync"/>.
/// </summary>
internal sealed class InMemoryApiKeyStore : IApiKeyStore
{
    private readonly ConcurrentDictionary<string, ApiKeyRecord> _keys = new(StringComparer.Ordinal);
    private int _lookups;

    public ConcurrentQueue<(Guid ApiKeyId, DateTimeOffset UsedAt)> Uses { get; } = new();

    public int Lookups => Volatile.Read(ref _lookups);

    public void Add(ApiKeyRecord key) => _keys[key.KeyHash] = key;

    public void Revoke(Guid apiKeyId, DateTimeOffset revokedAt)
    {
        var key = _keys.Values.Single(candidate => candidate.Id == apiKeyId);
        _keys[key.KeyHash] = key with { RevokedAt = revokedAt };
    }

    public Task<ApiKeyRecord?> FindByHashAsync(string keyHash, CancellationToken ct)
    {
        Interlocked.Increment(ref _lookups);
        return Task.FromResult(_keys.TryGetValue(keyHash, out var key) ? key : null);
    }

    public Task MarkUsedAsync(Guid apiKeyId, DateTimeOffset usedAt, CancellationToken ct)
    {
        Uses.Enqueue((apiKeyId, usedAt));
        return Task.CompletedTask;
    }
}
