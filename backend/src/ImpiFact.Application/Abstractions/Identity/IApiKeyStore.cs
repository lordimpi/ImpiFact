namespace ImpiFact.Application.Abstractions.Identity;

/// <summary>
/// Puerto de lectura de las API keys (llaves de acceso de las integraciones) guardadas en <c>api_keys</c>. La API
/// lo usa al autenticar cada solicitud; el adaptador sobre la tabla llega con los casos de uso de Identity.
/// </summary>
public interface IApiKeyStore
{
    /// <summary>
    /// Busca una llave por el resumen SHA-256 (función que convierte cualquier texto en 32 bytes de forma
    /// irreversible) de su texto completo, escrito en hexadecimal en minúsculas (<c>key_hash</c>). Devuelve la
    /// llave aunque esté revocada o vencida: quien llama decide.
    /// </summary>
    /// <returns>La llave, o <see langword="null"/> si no existe ninguna con ese resumen.</returns>
    Task<ApiKeyRecord?> FindByHashAsync(string keyHash, CancellationToken ct);

    /// <summary>
    /// Guarda la fecha del último uso (<c>last_used_at</c>). Se llama fuera de la solicitud, desde una cola en
    /// segundo plano, así que la implementación debe abrir y cerrar su propia unidad de trabajo.
    /// </summary>
    Task MarkUsedAsync(Guid apiKeyId, DateTimeOffset usedAt, CancellationToken ct);
}

/// <summary>Una API key tal como está guardada: nunca el texto claro, solo su resumen.</summary>
/// <param name="Id">Identificador de la fila en <c>api_keys</c>.</param>
/// <param name="CompanyId">Empresa dueña de la llave; nula para las llaves de plataforma.</param>
/// <param name="KeyHash">Resumen SHA-256 del texto completo de la llave, en hexadecimal (64 caracteres).</param>
/// <param name="Scopes">Alcances de la llave (ver <see cref="ApiScopes"/>).</param>
/// <param name="ExpiresAt">Vencimiento; nulo si no vence.</param>
/// <param name="RevokedAt">Fecha de revocación; nula si sigue activa.</param>
public sealed record ApiKeyRecord(
    Guid Id,
    Guid? CompanyId,
    string KeyHash,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt);
