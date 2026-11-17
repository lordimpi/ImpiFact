using System.Buffers.Text;
using System.Text.Json;
using FluentValidation;

namespace ImpiFact.Application.Features.Masters;

// Piezas comunes de los listados de clientes y productos (Arquitectura frontend, sección 7: /clientes y /productos).
// Toda lista pagina, ordena y filtra en el servidor: el panel nunca descarga la lista completa.

/// <summary>Qué filas trae el listado según su estado.</summary>
public enum MasterStatus
{
    /// <summary>Solo las activas (por defecto): las que se pueden usar al emitir.</summary>
    Active,

    /// <summary>Solo las desactivadas.</summary>
    Inactive,

    /// <summary>Todas.</summary>
    All,
}

/// <summary>Posición de una fila en el orden del listado: el valor de la columna de orden y el id, que desempata.</summary>
public readonly record struct MasterKey(string Value, Guid Id);

/// <summary>Una página pedida a un listado de maestros.</summary>
/// <param name="CompanyId">Empresa dueña de las filas; toda consulta la lleva en el WHERE.</param>
/// <param name="Search">Texto que debe contener alguna de las columnas de búsqueda, sin distinguir mayúsculas; nulo para no filtrar.</param>
/// <param name="SortField">Columna de orden, siempre una de las permitidas por el listado (nunca texto libre del cliente).</param>
/// <param name="Descending">Verdadero para el orden de mayor a menor.</param>
/// <param name="After">Última fila de la página anterior; nula para la primera página.</param>
/// <param name="Take">Máximo de filas que devuelve la consulta.</param>
public sealed record MasterPage(Guid CompanyId, string? Search, MasterStatus Status, string SortField, bool Descending, MasterKey? After, int Take);

/// <summary>
/// Parámetros de un listado ya leídos: el estado, el orden y la posición del cursor. Los valida
/// <see cref="MasterListRules"/> antes de llegar al caso de uso.
/// </summary>
internal static class MasterListing
{
    public const int DefaultLimit = 20;

    public const int MaxLimit = 100;

    public const int MaxSearchLength = 100;

    public static bool TryStatus(string? text, out MasterStatus status)
    {
        status = MasterStatus.Active;
        switch (text?.Trim().ToLowerInvariant())
        {
            case null or "" or "active":
                return true;
            case "inactive":
                status = MasterStatus.Inactive;
                return true;
            case "all":
                status = MasterStatus.All;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Lee <c>sort</c>: el nombre de una columna permitida, con <c>-</c> delante para el orden descendente.</summary>
    public static bool TrySort(string? text, IReadOnlyCollection<string> fields, string defaultField, out string field, out bool descending)
    {
        var value = text?.Trim() ?? "";
        descending = value.StartsWith('-');
        field = descending ? value[1..] : value;
        if (field.Length == 0 && !descending)
        {
            field = defaultField;
        }

        return fields.Contains(field);
    }

    /// <summary>Arma la página pedida; los parámetros ya pasaron por el validador.</summary>
    public static MasterPage Page(
        Guid companyId, string? search, string? status, string? sort, string? cursor, int? limit, IReadOnlyCollection<string> fields, string defaultField)
    {
        TryStatus(status, out var parsedStatus);
        TrySort(sort, fields, defaultField, out var field, out var descending);
        MasterKey? after = MasterCursor.TryDecode(cursor, out var cursorSort, out var key) && cursorSort == SortText(field, descending) ? key : null;
        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        return new MasterPage(companyId, term, parsedStatus, field, descending, after, (limit ?? DefaultLimit) + 1);
    }

    /// <summary>
    /// Recorta la fila de más que pidió la consulta y arma el cursor de la página siguiente con la última fila mostrada.
    /// </summary>
    public static (IReadOnlyList<T> Items, string? NextCursor) Cut<T>(IReadOnlyList<T> rows, MasterPage page, Func<T, MasterKey> keyOf)
    {
        var limit = page.Take - 1;
        var items = rows.Take(limit).ToList();
        var next = rows.Count > limit && items.Count > 0
            ? MasterCursor.Encode(SortText(page.SortField, page.Descending), keyOf(items[^1]))
            : null;
        return (items, next);
    }

    public static string SortText(string field, bool descending) => descending ? "-" + field : field;
}

/// <summary>
/// Cursor de los listados de maestros: el orden pedido, el valor de la columna de orden y el id de la última fila de la
/// página, en JSON y Base64 apto para URL. La página siguiente empieza justo después de esa posición, así no salta ni
/// repite filas aunque se agreguen clientes o productos, y no usa OFFSET. Para el cliente es un texto opaco; solo sirve
/// con el mismo <c>sort</c> con que se pidió.
/// </summary>
public static class MasterCursor
{
    private const int MaxLength = 1024;

    public static string Encode(string sort, MasterKey key) =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new CursorData(sort, key.Value, key.Id)));

    public static bool TryDecode(string? cursor, out string sort, out MasterKey key)
    {
        sort = "";
        key = default;
        if (string.IsNullOrEmpty(cursor) || cursor.Length > MaxLength || !Base64Url.IsValid(cursor))
        {
            return false;
        }

        try
        {
            var data = JsonSerializer.Deserialize<CursorData>(Base64Url.DecodeFromChars(cursor));
            if (data is not { S: { } s, V: { } v } || data.I == Guid.Empty)
            {
                return false;
            }

            sort = s;
            key = new MasterKey(v, data.I);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record CursorData(string? S, string? V, Guid I);
}

/// <summary>Reglas comunes de los parámetros de un listado, con las frases del resto de las consultas de la API.</summary>
internal static class MasterListRules
{
    public static void Apply<T>(
        AbstractValidator<T> validator,
        Func<T, string?> search,
        Func<T, string?> status,
        Func<T, string?> sort,
        Func<T, string?> cursor,
        Func<T, int?> limit,
        IReadOnlyCollection<string> fields,
        string defaultField)
    {
        validator.RuleFor(query => limit(query))
            .GreaterThanOrEqualTo(1).WithMessage(MasterMessages.MinNumber("limit", 1))
            .LessThanOrEqualTo(MasterListing.MaxLimit).WithMessage(MasterMessages.MaxNumber("limit", MasterListing.MaxLimit))
            .OverridePropertyName("limit");
        validator.RuleFor(query => search(query))
            .MaximumLength(MasterListing.MaxSearchLength).WithMessage(MasterMessages.MaxLength("search", MasterListing.MaxSearchLength))
            .OverridePropertyName("search");
        validator.RuleFor(query => status(query))
            .Must(text => MasterListing.TryStatus(text, out _)).WithMessage(MasterMessages.Invalid("status"))
            .OverridePropertyName("status");
        validator.RuleFor(query => sort(query))
            .Must(text => MasterListing.TrySort(text, fields, defaultField, out _, out _)).WithMessage(MasterMessages.Invalid("sort"))
            .OverridePropertyName("sort");
        validator.RuleFor(query => query)
            .Must(query => cursor(query) is not { } text || CursorMatches(text, sort(query), fields, defaultField))
            .WithMessage(MasterMessages.Invalid("cursor"))
            .OverridePropertyName("cursor");
    }

    private static bool CursorMatches(string cursor, string? sort, IReadOnlyCollection<string> fields, string defaultField) =>
        MasterCursor.TryDecode(cursor, out var cursorSort, out _)
        && MasterListing.TrySort(sort, fields, defaultField, out var field, out var descending)
        && cursorSort == MasterListing.SortText(field, descending);
}
