using System.Buffers.Text;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Identity.ApiKeys;

/// <summary>
/// <c>GET /api/v1/api-keys</c>: las API keys (llaves de acceso de las integraciones) de la empresa activa, o las de
/// plataforma si quien llama actúa por la plataforma, de la más nueva a la más antigua.
/// </summary>
/// <param name="Cursor">El <c>next_cursor</c> de la página anterior.</param>
/// <param name="Limit">Llaves por página; 20 si no se indica, máximo 100.</param>
public sealed record ListApiKeysQuery(string? Cursor, int? Limit) : ICompanyOptional;

/// <summary>Una página del listado de llaves.</summary>
public sealed record ApiKeyListResponse
{
    [JsonPropertyName("items")]
    public IReadOnlyList<ApiKeyItem> Items { get; init; } = [];

    /// <summary>Cursor de la página siguiente; nulo si no hay más.</summary>
    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; init; }
}

/// <summary>
/// Una llave guardada, sin el texto de la llave ni su resumen: el texto solo se mostró al crearla y después solo queda
/// <see cref="KeyPrefix"/> para reconocerla.
/// </summary>
public sealed record ApiKeyItem
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("key_prefix")]
    public string KeyPrefix { get; init; } = "";

    [JsonPropertyName("scopes")]
    public IReadOnlyList<string> Scopes { get; init; } = [];

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Vencimiento; nulo si no vence.</summary>
    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Último uso; nulo si nunca se usó. Se guarda en segundo plano y puede atrasarse unos segundos.</summary>
    [JsonPropertyName("last_used_at")]
    public DateTimeOffset? LastUsedAt { get; init; }

    [JsonPropertyName("is_revoked")]
    public bool IsRevoked { get; init; }

    [JsonPropertyName("revoked_at")]
    public DateTimeOffset? RevokedAt { get; init; }
}

internal sealed class ListApiKeysValidator : AbstractValidator<ListApiKeysQuery>
{
    public const int DefaultLimit = 20;

    public const int MaxLimit = 100;

    public ListApiKeysValidator()
    {
        RuleFor(query => query.Limit)
            .GreaterThanOrEqualTo(1).WithMessage("El campo limit debe ser como mínimo 1.")
            .LessThanOrEqualTo(MaxLimit).WithMessage($"El campo limit debe ser como máximo {MaxLimit}.")
            .OverridePropertyName("limit");
        RuleFor(query => query.Cursor)
            .Must(cursor => cursor is null || ApiKeyCursor.TryDecode(cursor, out _)).WithMessage("cursor es inválido.")
            .OverridePropertyName("cursor");
    }
}

/// <summary>
/// Las mismas reglas que crear y revocar: con empresa en el contexto solo se ven las llaves de esa empresa (y un
/// usuario del panel debe ser <c>owner</c> de ella); sin empresa, solo quien actúa por la plataforma, y ve las llaves
/// de plataforma. Pide una fila más de las que muestra para saber si hay página siguiente.
/// </summary>
internal sealed class ListApiKeysHandler(ITenantContext tenant, IUserStore users, IApiKeyReads keys)
    : IHandler<ListApiKeysQuery, ApiKeyListResponse>
{
    public async Task<Result<ApiKeyListResponse>> HandleAsync(ListApiKeysQuery request, CancellationToken ct)
    {
        var allowed = tenant.CompanyId is null
            ? await IdentityAccess.IsPlatformAsync(tenant, users, ct)
            : tenant.Actor != ActorType.User || await IdentityAccess.IsCompanyOwnerAsync(tenant, users, ct);
        if (!allowed)
        {
            return IdentityErrors.Forbidden;
        }

        var limit = request.Limit ?? ListApiKeysValidator.DefaultLimit;
        ApiKeyPosition? after = ApiKeyCursor.TryDecode(request.Cursor, out var position) ? position : null;
        var rows = await keys.ListAsync(new ApiKeyListFilter(tenant.CompanyId, after, limit + 1), ct);
        var page = rows.Take(limit).ToList();
        var last = page.LastOrDefault();
        return new ApiKeyListResponse
        {
            Items = page.Select(row => new ApiKeyItem
            {
                Id = row.Id,
                Name = row.Name,
                KeyPrefix = row.KeyPrefix,
                Scopes = row.Scopes,
                CreatedAt = row.CreatedAt,
                ExpiresAt = row.ExpiresAt,
                LastUsedAt = row.LastUsedAt,
                IsRevoked = row.RevokedAt is not null,
                RevokedAt = row.RevokedAt,
            }).ToList(),
            NextCursor = rows.Count > limit && last is not null ? ApiKeyCursor.Encode(new ApiKeyPosition(last.CreatedAt, last.Id)) : null,
        };
    }
}

/// <summary>
/// Cursor del listado de llaves: la posición <c>(created_at, id)</c> de la última llave de la página, en Base64 apto
/// para URL, con el mismo formato que el cursor de los avisos. La página siguiente empieza justo después, así no salta
/// ni repite filas aunque se creen llaves nuevas, y no usa OFFSET. Para el cliente es un texto opaco.
/// </summary>
internal static class ApiKeyCursor
{
    private const char Separator = '.';

    public static string Encode(ApiKeyPosition position)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"{position.CreatedAt.UtcTicks}{Separator}{position.Id:N}");
        return Base64Url.EncodeToString(Encoding.ASCII.GetBytes(text));
    }

    public static bool TryDecode(string? cursor, out ApiKeyPosition position)
    {
        position = default;
        if (string.IsNullOrEmpty(cursor) || cursor.Length > 128 || !Base64Url.IsValid(cursor))
        {
            return false;
        }

        var parts = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(cursor)).Split(Separator);
        if (parts.Length != 2
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTimeOffset.MaxValue.UtcTicks
            || !Guid.TryParseExact(parts[1], "N", out var id))
        {
            return false;
        }

        position = new ApiKeyPosition(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}
