using ImpiFact.Application.Abstractions.Identity;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>
/// Almacén provisional mientras no existe el adaptador sobre la tabla <c>api_keys</c> (casos de uso de Identity):
/// no conoce ninguna llave, así que rechaza todas. Se registra con <c>TryAdd</c> para que el adaptador real lo
/// reemplace sin tocar este registro.
/// </summary>
internal sealed class EmptyApiKeyStore : IApiKeyStore
{
    public Task<ApiKeyRecord?> FindByHashAsync(string keyHash, CancellationToken ct) => Task.FromResult<ApiKeyRecord?>(null);

    public Task MarkUsedAsync(Guid apiKeyId, DateTimeOffset usedAt, CancellationToken ct) => Task.CompletedTask;
}
