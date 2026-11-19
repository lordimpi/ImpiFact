using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using FluentValidation;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Catalogs;

/// <summary><c>GET /api/v1/catalogs/{name}</c>: filas de un catálogo DIAN para los formularios del panel.</summary>
/// <param name="Name">Nombre público del catálogo (<see cref="CatalogDefinitions"/>), por ejemplo <c>municipalities</c>.</param>
/// <param name="Q">Texto que contiene el código o el nombre, sin distinguir mayúsculas ni tildes.</param>
/// <param name="Cursor">El <c>next_cursor</c> de la página anterior; solo en los catálogos paginados.</param>
/// <param name="Limit">Filas por página en los catálogos paginados: 50 si no llega, máximo 200.</param>
/// <param name="IncludeInactive"><c>true</c> para incluir las filas inactivas; por defecto solo las activas.</param>
/// <param name="DepartmentId">Id del departamento (catálogo <c>departments</c>); solo filtra <c>municipalities</c>.</param>
public sealed record ListCatalogQuery(
    string Name, string? Q, string? Cursor, int? Limit, string? IncludeInactive, string? DepartmentId = null) : ICompanyOptional;

/// <summary>Una página de un catálogo.</summary>
public sealed record CatalogListResponse
{
    /// <summary>Nombre público del catálogo.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>
    /// Versión del catálogo completo: cambia cuando cambia cualquier fila (también al activarla o desactivarla). Es el
    /// <c>ETag</c> de la respuesta.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("items")]
    public IReadOnlyList<CatalogItemResponse> Items { get; init; } = [];

    /// <summary>Cursor de la página siguiente; nulo en la última y siempre nulo en los catálogos que salen completos.</summary>
    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; init; }
}

/// <summary>Una fila de un catálogo: el id es el que esperan los campos <c>*_id</c> de la emisión y de los maestros.</summary>
public sealed record CatalogItemResponse
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    /// <summary>Código DIAN de la fila.</summary>
    [JsonPropertyName("code")]
    public string Code { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("is_active")]
    public bool IsActive { get; init; }

    /// <summary>Departamento del municipio (catálogo <c>departments</c>); solo en <c>municipalities</c>.</summary>
    [JsonPropertyName("department_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DepartmentId { get; init; }

    /// <summary>País del departamento (catálogo <c>countries</c>); solo en <c>departments</c>.</summary>
    [JsonPropertyName("country_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CountryId { get; init; }

    /// <summary>Nombre largo del impuesto; solo en <c>taxes</c>.</summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }
}

/// <summary>Reglas de los parámetros; un parámetro no válido responde 422 con <c>errors</c> por campo.</summary>
internal sealed class ListCatalogQueryValidator : AbstractValidator<ListCatalogQuery>
{
    public ListCatalogQueryValidator()
    {
        RuleFor(query => query.Limit)
            .GreaterThanOrEqualTo(1).WithMessage("El campo limit debe ser como mínimo 1.")
            .LessThanOrEqualTo(CatalogPaging.MaxLimit).WithMessage($"El campo limit debe ser como máximo {CatalogPaging.MaxLimit}.")
            .OverridePropertyName("limit");
        RuleFor(query => query.Q)
            .MaximumLength(CatalogPaging.MaxSearchLength).WithMessage($"q no debe ser mayor que {CatalogPaging.MaxSearchLength} caracteres.")
            .Must(text => text is null || !text.Any(character => character < ' ')).WithMessage("q no debe contener caracteres de control.")
            .OverridePropertyName("q");
        RuleFor(query => query.Cursor)
            .Must(cursor => cursor is null || CatalogPaging.TryDecode(cursor, out _)).WithMessage("cursor es inválido.")
            .OverridePropertyName("cursor");
        RuleFor(query => query.IncludeInactive)
            .Must(text => CatalogPaging.TryFlag(text, out _)).WithMessage("include inactive es inválido.")
            .OverridePropertyName("include_inactive");
        RuleFor(query => query.DepartmentId)
            .Must(text => CatalogPaging.TryDepartment(text, out _)).WithMessage("department id debe ser un número entero mayor que 0.")
            .Must((query, text) => text is null || query.Name == CatalogPaging.MunicipalitiesName)
            .WithMessage($"department id solo aplica al catálogo {CatalogPaging.MunicipalitiesName}.")
            .OverridePropertyName("department_id");
    }
}

/// <summary>
/// Lee el catálogo completo (el más largo tiene unas 1.100 filas), filtra y pagina en memoria. La búsqueda compara sin
/// mayúsculas ni tildes («bogota» encuentra «Bogotá»), cosa que la base no hace sin extensiones. El orden es siempre por
/// id, el mismo del PHP; el cursor es el id de la última fila mostrada, así una página nunca salta ni repite filas.
/// </summary>
internal sealed class ListCatalogHandler(ICatalogRows rows) : IHandler<ListCatalogQuery, CatalogListResponse>
{
    public async Task<Result<CatalogListResponse>> HandleAsync(ListCatalogQuery request, CancellationToken ct)
    {
        if (CatalogDefinitions.Find(request.Name) is not { } definition)
        {
            return new Error("catalog.not_found", $"El catálogo «{request.Name}» no existe.", ErrorType.NotFound);
        }

        var all = await rows.ListAsync(definition.Kind, ct);
        CatalogPaging.TryFlag(request.IncludeInactive, out var includeInactive);
        var term = string.IsNullOrWhiteSpace(request.Q) ? null : CatalogPaging.Fold(request.Q.Trim());
        IEnumerable<CatalogRow> matches = all.Where(row => includeInactive || row.IsActive);
        if (CatalogPaging.TryDepartment(request.DepartmentId, out var departmentId) && departmentId is { } department)
        {
            matches = matches.Where(row => row.DepartmentId == department);
        }

        if (term is not null)
        {
            matches = matches.Where(row => CatalogPaging.Fold(row.Code).Contains(term, StringComparison.Ordinal)
                || CatalogPaging.Fold(row.Name).Contains(term, StringComparison.Ordinal));
        }

        matches = matches.OrderBy(row => row.Id);
        string? next = null;
        if (definition.Paged)
        {
            if (request.Cursor is { } cursor && CatalogPaging.TryDecode(cursor, out var after))
            {
                matches = matches.Where(row => row.Id > after);
            }

            var limit = request.Limit ?? CatalogPaging.DefaultLimit;
            var page = matches.Take(limit + 1).ToList();
            next = page.Count > limit ? CatalogPaging.Encode(page[limit - 1].Id) : null;
            matches = page.Take(limit);
        }

        return new CatalogListResponse
        {
            Name = definition.Name,
            Version = CatalogPaging.Version(all),
            Items = matches.Select(row => new CatalogItemResponse
            {
                Id = row.Id,
                Code = row.Code,
                Name = row.Name,
                IsActive = row.IsActive,
                DepartmentId = row.DepartmentId,
                CountryId = row.CountryId,
                Description = row.Description,
            }).ToList(),
            NextCursor = next,
        };
    }
}

/// <summary>Piezas del listado: tamaño de página, cursor, búsqueda sin tildes y versión del catálogo.</summary>
internal static class CatalogPaging
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 200;

    public const int MaxSearchLength = 100;

    /// <summary>Único catálogo que se filtra por departamento.</summary>
    public const string MunicipalitiesName = "municipalities";

    /// <summary>Forma de la respuesta; si cambia, cambia la versión y los ETag guardados dejan de servir.</summary>
    private const string Format = "catalog-v1";

    private const string CursorPrefix = "after:";

    /// <summary>Cursor opaco para el cliente: el id de la última fila, en Base64 apto para URL.</summary>
    public static string Encode(int afterId) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(CursorPrefix + afterId.ToString(CultureInfo.InvariantCulture)));

    public static bool TryDecode(string cursor, out int afterId)
    {
        afterId = 0;
        if (cursor.Length is 0 or > 64 || !Base64Url.IsValid(cursor))
        {
            return false;
        }

        var text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
        return text.StartsWith(CursorPrefix, StringComparison.Ordinal)
            && int.TryParse(text.AsSpan(CursorPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out afterId)
            && afterId > 0;
    }

    /// <summary>Lee <c>include_inactive</c>: vacío o <c>false</c> es falso, <c>true</c> es verdadero.</summary>
    public static bool TryFlag(string? text, out bool value)
    {
        value = false;
        switch (text?.Trim().ToLowerInvariant())
        {
            case null or "" or "false":
                return true;
            case "true":
                value = true;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Lee <c>department_id</c>: vacío es sin filtro; si llega, un entero mayor que 0.</summary>
    public static bool TryDepartment(string? text, out int? departmentId)
    {
        departmentId = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 1)
        {
            return false;
        }

        departmentId = value;
        return true;
    }

    /// <summary>El texto en minúsculas y sin tildes ni diéresis (la «ñ» queda como «n»), para comparar.</summary>
    public static string Fold(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var folded = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                folded.Append(character);
            }
        }

        return folded.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    /// <summary>Primeros 16 bytes del SHA-256 de todas las filas, en hexadecimal (32 caracteres).</summary>
    public static string Version(IReadOnlyList<CatalogRow> rows)
    {
        var text = new StringBuilder(Format).Append('\n');
        foreach (var row in rows.OrderBy(row => row.Id))
        {
            text.Append(row.Id.ToString(CultureInfo.InvariantCulture)).Append('\u001f')
                .Append(row.Code).Append('\u001f')
                .Append(row.Name).Append('\u001f')
                .Append(row.IsActive ? '1' : '0').Append('\u001f')
                .Append(row.DepartmentId?.ToString(CultureInfo.InvariantCulture)).Append('\u001f')
                .Append(row.CountryId?.ToString(CultureInfo.InvariantCulture)).Append('\u001f')
                .Append(row.Description).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())).AsSpan(0, 16));
    }
}
