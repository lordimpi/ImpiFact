namespace ImpiFact.Application.Abstractions.Identity;

/// <summary>
/// Puerto de las API keys (llaves de acceso de las integraciones) guardadas en <c>api_keys</c>. La API lo usa al
/// autenticar cada solicitud; los casos de uso de Identity, para crear y revocar llaves.
/// </summary>
/// <remarks>
/// Si la base de datos no responde, las operaciones lanzan <see cref="CredentialStoreUnavailableException"/>: la
/// API lo traduce a un 503 sin detalles internos, para que el cliente reintente en lugar de recibir un 500.
/// </remarks>
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

    /// <returns>La llave con ese id, aunque esté revocada o vencida; <see langword="null"/> si no existe.</returns>
    Task<ApiKeyRecord?> FindByIdAsync(Guid apiKeyId, CancellationToken ct);

    /// <summary>Agrega una llave nueva. Se guarda al confirmar la transacción del caso de uso.</summary>
    Task AddAsync(NewApiKey key, CancellationToken ct);

    /// <summary>Marca la llave como revocada (<c>revoked_at</c>). Se guarda al confirmar la transacción del caso de uso.</summary>
    Task RevokeAsync(Guid apiKeyId, DateTimeOffset revokedAt, CancellationToken ct);

    /// <summary>
    /// Marca revocadas todas las llaves de la empresa que siguen sin revocar (vencidas incluidas), cuando la plataforma
    /// la desactiva. Se guarda al confirmar la transacción del caso de uso y se revierte con ella. La revocación es
    /// definitiva: reactivar la empresa no devuelve estas llaves.
    /// </summary>
    /// <returns>El resumen (<c>key_hash</c>) de cada llave revocada, para sacarla de la caché.</returns>
    Task<IReadOnlyList<string>> RevokeAllActiveForCompanyAsync(Guid companyId, DateTimeOffset revokedAt, CancellationToken ct);
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

/// <summary>Una API key nueva para guardar: solo el resumen y el prefijo para mostrar, nunca el texto claro.</summary>
/// <param name="Id">Identificador de la fila (uuid v7 generado por la aplicación).</param>
/// <param name="CompanyId">Empresa dueña; nula para una llave de plataforma.</param>
/// <param name="Name">Nombre para reconocerla en el panel.</param>
/// <param name="KeyPrefix">Primeros caracteres de la llave (<c>key_prefix</c>); no es secreto.</param>
/// <param name="KeyHash">Resumen SHA-256 en hexadecimal (<c>key_hash</c>).</param>
/// <param name="Scopes">Alcances; se guardan separados por coma.</param>
/// <param name="ExpiresAt">Vencimiento; nulo si no vence.</param>
/// <param name="CreatedBy">Usuario del panel que la creó; nulo si la creó otra API key o el sistema.</param>
public sealed record NewApiKey(
    Guid Id,
    Guid? CompanyId,
    string Name,
    string KeyPrefix,
    string KeyHash,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? ExpiresAt,
    Guid? CreatedBy);
