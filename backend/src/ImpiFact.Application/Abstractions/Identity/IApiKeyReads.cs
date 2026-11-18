namespace ImpiFact.Application.Abstractions.Identity;

/// <summary>
/// Listado de API keys para el panel (<c>/llaves-api</c>). Solo lee los datos que se pueden mostrar: nunca el resumen
/// SHA-256 (<c>key_hash</c>) ni, por supuesto, el texto de la llave, que no se guarda. Lo implementa Infrastructure con
/// SQL de solo lectura y la empresa en el WHERE.
/// </summary>
public interface IApiKeyReads
{
    /// <summary>
    /// Llaves de la empresa (o de plataforma si <see cref="ApiKeyListFilter.CompanyId"/> es nulo), revocadas o no, de
    /// la más nueva a la más antigua por <c>(created_at, id)</c>.
    /// </summary>
    Task<IReadOnlyList<ApiKeySummary>> ListAsync(ApiKeyListFilter filter, CancellationToken ct);
}

/// <summary>Posición de una llave en el orden del listado.</summary>
public readonly record struct ApiKeyPosition(DateTimeOffset CreatedAt, Guid Id);

/// <summary>Una página pedida al listado de llaves.</summary>
/// <param name="CompanyId">Empresa dueña de las llaves; nulo para las llaves de plataforma (sin empresa).</param>
/// <param name="After">Última llave de la página anterior; nula para la primera página.</param>
/// <param name="Take">Máximo de filas que devuelve la consulta.</param>
public sealed record ApiKeyListFilter(Guid? CompanyId, ApiKeyPosition? After, int Take);

/// <summary>Lo que se muestra de una llave guardada.</summary>
/// <param name="KeyPrefix">Inicio visible de la llave (por ejemplo <c>apk_1a2b3c4d</c>); no es secreto.</param>
/// <param name="LastUsedAt">Último uso registrado; se guarda en segundo plano, así que puede atrasarse unos segundos.</param>
public sealed record ApiKeySummary(
    Guid Id,
    string Name,
    string KeyPrefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt);
