using FluentValidation;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Features.Documents.Queries;

/// <summary>
/// Filtros que comparten el listado de documentos (<c>GET /api/v1/documents</c>) y su exportación CSV (valores
/// separados por punto y coma): así la exportación trae exactamente las filas que el panel muestra con esos filtros.
/// </summary>
public interface IDocumentFilters
{
    /// <summary>Tipo de documento (1 factura, 4 nota crédito, 5 nota débito…).</summary>
    int? DocumentTypeId { get; }

    /// <summary>
    /// Familia en palabras: <c>invoice</c>, <c>credit_note</c>, <c>debit_note</c>, <c>support_document</c> o
    /// <c>support_document_adjustment_note</c> (ver <see cref="DocumentKinds"/>).
    /// </summary>
    string? Kind { get; }

    /// <summary>Uno o varios estados por su nombre, separados por coma y sin distinguir mayúsculas.</summary>
    string? Status { get; }

    /// <summary>Primer día de emisión incluido, <c>yyyy-MM-dd</c> en hora Colombia.</summary>
    string? From { get; }

    /// <summary>Último día de emisión incluido.</summary>
    string? To { get; }

    /// <summary>Texto que contiene el número de identificación o el nombre del adquiriente.</summary>
    string? Customer { get; }

    /// <summary>Número completo con prefijo (<c>SETP990000001</c>) o solo los dígitos.</summary>
    string? Number { get; }
}

/// <summary>
/// Nombres de las familias que muestra el panel y el tipo de documento de cada una (los de la emisión:
/// <c>IssueSpec</c>). Los demás tipos (nómina, exportación, POS) no tienen nombre aquí: su <c>kind</c> sale nulo, se
/// filtran por <c>document_type_id</c> y el tablero los cuenta como <c>other</c>. The support document and its adjustment
/// note have their own names (phase 7a, T4b), like the credit and debit notes: their rows say <c>support_document</c> and
/// <c>support_document_adjustment_note</c>, and the dashboard counts them apart.
/// </summary>
public static class DocumentKinds
{
    public const string Invoice = "invoice";

    public const string CreditNote = "credit_note";

    public const string DebitNote = "debit_note";

    /// <summary>Support document (type 11).</summary>
    public const string SupportDocument = "support_document";

    /// <summary>Adjustment note to a support document (type 13).</summary>
    public const string SupportDocumentAdjustmentNote = "support_document_adjustment_note";

    /// <summary>Cada familia con su tipo de documento, en el orden en que se muestran.</summary>
    public static IReadOnlyList<(string Name, int DocumentTypeId)> All { get; } = [(Invoice, 1), (CreditNote, 4), (DebitNote, 5)];

    /// <summary>Every named family: the invoice ones plus the support-document ones; the <c>kind</c> filter accepts them all.</summary>
    public static IReadOnlyList<(string Name, int DocumentTypeId)> Filterable { get; } =
        [.. All, (SupportDocument, 11), (SupportDocumentAdjustmentNote, 13)];

    /// <summary>Tipo de documento de la familia (sin distinguir mayúsculas); nulo si el nombre no es una familia.</summary>
    public static int? TypeIdOf(string? kind) =>
        Filterable.Where(entry => string.Equals(entry.Name, kind, StringComparison.OrdinalIgnoreCase)).Select(entry => (int?)entry.DocumentTypeId).FirstOrDefault();

    /// <summary>Nombre de la familia del tipo de documento; nulo para los tipos sin nombre.</summary>
    public static string? NameOf(int documentTypeId) =>
        Filterable.Where(entry => entry.DocumentTypeId == documentTypeId).Select(entry => entry.Name).FirstOrDefault();
}

/// <summary>Lectura de los filtros ya validados.</summary>
internal static class DocumentFilters
{
    /// <summary>Largo máximo del texto de búsqueda del adquiriente.</summary>
    public const int CustomerMaxLength = 100;

    /// <summary>Largo de la columna <c>documents.full_number</c>.</summary>
    public const int NumberMaxLength = 30;

    /// <summary>
    /// Arma el filtro de la consulta. Un texto vacío o solo con espacios en <c>customer</c> o <c>number</c> no filtra,
    /// porque es lo que envía el cuadro de búsqueda del panel cuando está vacío.
    /// </summary>
    public static DocumentListFilter ToFilter(IDocumentFilters query, Guid companyId, DocumentKey? after, int take)
    {
        DateTimeOffset? issuedFrom = ColombiaDates.TryParse(query.From, out var from) ? ColombiaDates.StartOf(from) : null;
        DateTimeOffset? issuedUntil = ColombiaDates.TryParse(query.To, out var to) ? ColombiaDates.StartOf(to.AddDays(1)) : null;
        return new DocumentListFilter(
            companyId, query.DocumentTypeId ?? DocumentKinds.TypeIdOf(query.Kind), ParseStatuses(query.Status) ?? [], issuedFrom, issuedUntil, after, take)
        {
            Customer = Blank(query.Customer) ? null : query.Customer!.Trim(),
            Number = Blank(query.Number) ? null : query.Number!.Trim(),
        };
    }

    /// <summary>
    /// Estados separados por coma, por su nombre y sin distinguir mayúsculas; sin repetidos. Nulo si alguno no es un
    /// estado o si falta un nombre entre dos comas. No se aceptan números: <c>Enum.TryParse</c> aceptaría cualquier
    /// número, incluso uno que no es un estado.
    /// </summary>
    public static IReadOnlyList<DocumentStatus>? ParseStatuses(string? status)
    {
        if (status is null)
        {
            return [];
        }

        var statuses = new List<DocumentStatus>();
        foreach (var name in status.Split(','))
        {
            if (Enum.GetValues<DocumentStatus>().Cast<DocumentStatus?>()
                    .FirstOrDefault(value => string.Equals(value.ToString(), name.Trim(), StringComparison.OrdinalIgnoreCase)) is not { } parsed)
            {
                return null;
            }

            if (!statuses.Contains(parsed))
            {
                statuses.Add(parsed);
            }
        }

        return statuses;
    }

    public static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
}

/// <summary>Reglas de los filtros comunes; cada validador de consulta las incluye con <c>Include</c>.</summary>
internal sealed class DocumentFiltersValidator : AbstractValidator<IDocumentFilters>
{
    public DocumentFiltersValidator()
    {
        RuleFor(query => query.Status)
            .Must(status => DocumentFilters.ParseStatuses(status) is not null)
            .OverridePropertyName("status")
            .WithMessage(QueryMessages.Invalid("status"));
        RuleFor(query => query.Kind)
            .Must(kind => kind is null || DocumentKinds.TypeIdOf(kind) is not null)
            .OverridePropertyName("kind")
            .WithMessage(QueryMessages.Invalid("kind"));
        RuleFor(query => query)
            .Must(query => query.DocumentTypeId is not { } typeId || DocumentKinds.TypeIdOf(query.Kind) is not { } kindTypeId || typeId == kindTypeId)
            .OverridePropertyName("kind")
            .WithMessage(QueryMessages.Mismatch("kind", "document_type_id"));
        RuleFor(query => query.From)
            .Must(value => value is null || ColombiaDates.TryParse(value, out _))
            .OverridePropertyName("from")
            .WithMessage(QueryMessages.Date("from"));
        RuleFor(query => query.To)
            .Must(value => value is null || ColombiaDates.TryParse(value, out _))
            .OverridePropertyName("to")
            .WithMessage(QueryMessages.Date("to"));
        RuleFor(query => query)
            .Must(query => !ColombiaDates.TryParse(query.From, out var from) || !ColombiaDates.TryParse(query.To, out var to) || from <= to)
            .OverridePropertyName("to")
            .WithMessage(QueryMessages.AfterOrEqual("to", "from"));
        RuleFor(query => query.Customer)
            .Must(value => DocumentFilters.Blank(value) || value!.Trim().Length <= DocumentFilters.CustomerMaxLength)
            .OverridePropertyName("customer")
            .WithMessage(QueryMessages.MaxLength("customer", DocumentFilters.CustomerMaxLength));
        RuleFor(query => query.Number)
            .Must(value => DocumentFilters.Blank(value) || IsDocumentNumber(value!.Trim()))
            .OverridePropertyName("number")
            .WithMessage(QueryMessages.Invalid("number"));
    }

    /// <summary>Solo letras sin tilde y dígitos, como el prefijo y el número que se guardan en <c>full_number</c>.</summary>
    private static bool IsDocumentNumber(string value) =>
        value.Length <= DocumentFilters.NumberMaxLength && value.All(char.IsAsciiLetterOrDigit);
}
