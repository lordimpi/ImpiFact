using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Companies.ListPlatformCompanies;

/// <summary>
/// <c>GET /api/v1/platform/companies</c>: todas las empresas de la plataforma, por razón social y página por página,
/// con el plan vigente de cada una. Operación de plataforma.
/// </summary>
/// <param name="Search">Texto que contiene el NIT o la razón social, sin distinguir mayúsculas.</param>
/// <param name="Cursor">El <c>next_cursor</c> de la página anterior.</param>
/// <param name="Limit">Empresas por página; 20 si no se indica, máximo 100.</param>
public sealed record ListPlatformCompaniesQuery(string? Search, string? Cursor, int? Limit) : ICompanyOptional;

/// <summary>Una página del listado de empresas.</summary>
public sealed record PlatformCompanyListResponse
{
    [JsonPropertyName("items")]
    public IReadOnlyList<PlatformCompanyItem> Items { get; init; } = [];

    /// <summary>Cursor de la página siguiente; nulo si no hay más.</summary>
    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; init; }
}

/// <summary>Una empresa con los mismos nombres de <c>listMyCompanies</c> y su plan vigente.</summary>
public sealed record PlatformCompanyItem
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>Razón social.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("nit")]
    public string Nit { get; init; } = "";

    [JsonPropertyName("dv")]
    public string Dv { get; init; } = "";

    [JsonPropertyName("email")]
    public string Email { get; init; } = "";

    [JsonPropertyName("is_active")]
    public bool IsActive { get; init; }

    /// <summary>Plan contratado activo (el más reciente si hubiera varios); nulo si no tiene plan.</summary>
    [JsonPropertyName("plan_id")]
    public int? PlanId { get; init; }

    [JsonPropertyName("plan_name")]
    public string? PlanName { get; init; }
}

internal sealed class ListPlatformCompaniesValidator : AbstractValidator<ListPlatformCompaniesQuery>
{
    public const int DefaultLimit = 20;

    public const int MaxLimit = 100;

    public const int MaxSearchLength = 100;

    public ListPlatformCompaniesValidator()
    {
        RuleFor(query => query.Limit)
            .GreaterThanOrEqualTo(1).WithMessage(PhpMessages.MinNumber("limit", 1))
            .LessThanOrEqualTo(MaxLimit).WithMessage($"El campo limit debe ser como máximo {MaxLimit}.")
            .OverridePropertyName("limit");
        // Un carácter de control (por ejemplo U+0000) haría fallar el parámetro en PostgreSQL.
        RuleFor(query => query.Search)
            .MaximumLength(MaxSearchLength).WithMessage(PhpMessages.MaxLength("search", MaxSearchLength))
            .Must(search => search is null || !search.Any(char.IsControl)).WithMessage(PhpMessages.Exists("search"))
            .OverridePropertyName("search");
        RuleFor(query => query.Cursor)
            .Must(cursor => cursor is null || PlatformCompanyCursor.TryDecode(cursor, out _)).WithMessage(PhpMessages.Exists("cursor"))
            .OverridePropertyName("cursor");
    }
}

/// <summary>
/// Pide una fila más de las que muestra para saber si hay página siguiente. Las políticas de la API ya exigen
/// <c>platform:*</c> sin empresa; aquí se confirma otra vez, por si una credencial de empresa llegara hasta el caso de uso.
/// </summary>
internal sealed class ListPlatformCompaniesHandler(IPlatformCompanyReads companies, ITenantContext tenant)
    : IHandler<ListPlatformCompaniesQuery, PlatformCompanyListResponse>
{
    /// <summary>El mismo código que el alta reservada a la plataforma, con el texto de esta consulta.</summary>
    internal static readonly Error PlatformOnly =
        new(CompanyErrors.PlatformOnly.Code, "Solo la plataforma puede ver todas las empresas.", ErrorType.Forbidden);

    public async Task<Result<PlatformCompanyListResponse>> HandleAsync(ListPlatformCompaniesQuery request, CancellationToken ct)
    {
        var isPlatform = tenant.CompanyId is null
            && (tenant.Actor is ActorType.Platform or ActorType.System
                || (tenant.Actor == ActorType.User && tenant.Scopes.Contains(ApiScopes.Platform)));
        if (!isPlatform)
        {
            return PlatformOnly;
        }

        var limit = request.Limit ?? ListPlatformCompaniesValidator.DefaultLimit;
        PlatformCompanyPosition? after = PlatformCompanyCursor.TryDecode(request.Cursor, out var position) ? position : null;
        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        var rows = await companies.ListAsync(new PlatformCompanyFilter(search, after, limit + 1), ct);
        var page = rows.Take(limit).ToList();
        var last = page.LastOrDefault();
        return new PlatformCompanyListResponse
        {
            Items = page.Select(row => new PlatformCompanyItem
            {
                Id = row.Id,
                Name = row.Name,
                Nit = row.Nit,
                Dv = row.Dv,
                Email = row.Email,
                IsActive = row.IsActive,
                PlanId = row.PlanId,
                PlanName = row.PlanName,
            }).ToList(),
            NextCursor = rows.Count > limit && last is not null
                ? PlatformCompanyCursor.Encode(new PlatformCompanyPosition(last.SortName, last.Id))
                : null,
        };
    }
}

/// <summary>
/// Cursor del listado de empresas: la razón social en minúsculas y el id de la última empresa de la página, en JSON y
/// Base64 apto para URL. La página siguiente empieza justo después de esa posición, sin OFFSET. Para el cliente es un
/// texto opaco.
/// </summary>
internal static class PlatformCompanyCursor
{
    private const int MaxLength = 1024;

    public static string Encode(PlatformCompanyPosition position) =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new CursorData(position.SortName, position.Id)));

    public static bool TryDecode(string? cursor, out PlatformCompanyPosition position)
    {
        position = default;
        if (string.IsNullOrEmpty(cursor) || cursor.Length > MaxLength || !Base64Url.IsValid(cursor))
        {
            return false;
        }

        try
        {
            var data = JsonSerializer.Deserialize<CursorData>(Base64Url.DecodeFromChars(cursor));
            // El nombre va como parámetro de la consulta: con caracteres de control PostgreSQL fallaría.
            if (data is not { N: { } name } || data.I == Guid.Empty || name.Any(char.IsControl))
            {
                return false;
            }

            position = new PlatformCompanyPosition(name, data.I);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record CursorData(string? N, Guid I);
}
