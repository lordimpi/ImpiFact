using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>
/// Valida una API key presentada en el encabezado Bearer (Arquitectura detallada, sección 9.2): calcula su resumen
/// SHA-256, la busca (con caché de 60 segundos), revisa <c>revoked_at</c> y <c>expires_at</c> y anota el uso sin
/// bloquear. Nunca escribe la llave en los registros; para identificarla usa el id de la fila.
/// </summary>
public sealed class ApiKeyAuthenticator
{
    /// <summary>
    /// Largo máximo aceptado. Una llave real mide 47 caracteres; el margen deja espacio a otros prefijos y evita
    /// calcular resúmenes de textos enormes.
    /// </summary>
    public const int MaxLength = 256;

    private readonly IApiKeyStore _store;
    private readonly ApiKeyCache _cache;
    private readonly ApiKeyUsageRecorder _usage;
    private readonly IClock _clock;
    private readonly ApiKeyFormat _format;
    private readonly ILogger<ApiKeyAuthenticator> _logger;

    internal ApiKeyAuthenticator(
        IApiKeyStore store,
        ApiKeyCache cache,
        ApiKeyUsageRecorder usage,
        IClock clock,
        ApiKeyFormat format,
        ILogger<ApiKeyAuthenticator> logger)
    {
        _store = store;
        _cache = cache;
        _usage = usage;
        _clock = clock;
        _format = format;
        _logger = logger;
    }

    /// <returns>La llave si es válida y está vigente; <see langword="null"/> en cualquier otro caso.</returns>
    public async Task<ApiKeyRecord?> AuthenticateAsync(string presentedKey, CancellationToken ct)
    {
        if (!_format.IsApiKey(presentedKey) || presentedKey.Length > MaxLength)
        {
            return null;
        }

        var hash = CredentialHash.Sha256Hex(presentedKey);
        var key = await _cache.GetOrLoadAsync(hash, _store, ct);

        // Defensa adicional: el almacén busca por igualdad, pero se vuelve a comparar en tiempo constante por si una
        // intercalación de la base (reglas de comparación de texto) llegara a tratar distinto mayúsculas y minúsculas.
        if (key is null || !CredentialHash.FixedTimeEquals(key.KeyHash, hash))
        {
            _logger.LogInformation("Rejected an API key that does not exist.");
            return null;
        }

        var now = _clock.UtcNow;
        if (key.RevokedAt is { } revokedAt && revokedAt <= now)
        {
            _logger.LogInformation("Rejected revoked API key {ApiKeyId}.", key.Id);
            return null;
        }

        if (key.ExpiresAt is { } expiresAt && expiresAt <= now)
        {
            _logger.LogInformation("Rejected expired API key {ApiKeyId}.", key.Id);
            return null;
        }

        _usage.Record(key.Id, now);
        return key;
    }
}
