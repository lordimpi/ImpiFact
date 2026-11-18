namespace ImpiFact.Application.Features.Catalogs;

/// <summary>
/// Catálogos DIAN (Dirección de Impuestos y Aduanas Nacionales: las listas de códigos del anexo técnico) que publica
/// <c>GET /api/v1/catalogs/{name}</c> para los formularios del panel. Solo están los sembrados que usan los formularios
/// de empresa, clientes, productos y emisión; los demás (eventos RADIAN, motivos de rechazo y otros) no se publican.
/// </summary>
public enum CatalogKind
{
    /// <summary><c>type_document_identifications</c>: NIT, cédula y demás.</summary>
    IdentificationTypes,

    /// <summary><c>type_organizations</c>: persona jurídica o natural.</summary>
    OrganizationTypes,

    /// <summary><c>type_regimes</c>.</summary>
    Regimes,

    /// <summary><c>type_liabilities</c>: responsabilidades fiscales.</summary>
    Liabilities,

    /// <summary><c>municipalities</c>, con su departamento.</summary>
    Municipalities,

    /// <summary><c>departments</c>, con su país.</summary>
    Departments,

    /// <summary><c>countries</c>.</summary>
    Countries,

    /// <summary><c>unit_measures</c>.</summary>
    UnitMeasures,

    /// <summary><c>taxes</c>, con su descripción.</summary>
    Taxes,

    /// <summary><c>payment_forms</c>: contado o crédito.</summary>
    PaymentForms,

    /// <summary><c>payment_methods</c>.</summary>
    PaymentMethods,

    /// <summary><c>type_documents</c>.</summary>
    DocumentTypes,

    /// <summary><c>type_currencies</c>.</summary>
    Currencies,

    /// <summary><c>type_item_identifications</c>: estándar del código de un producto.</summary>
    ItemIdentificationTypes,

    /// <summary><c>credit_note_discrepancy_responses</c>: conceptos de corrección de la nota crédito.</summary>
    CreditNoteDiscrepancyResponses,

    /// <summary><c>debit_note_discrepancy_responses</c>: conceptos de corrección de la nota débito.</summary>
    DebitNoteDiscrepancyResponses,
}

/// <summary>Nombre público de un catálogo en la ruta y si se pagina por cursor.</summary>
/// <param name="Paged">
/// Verdadero en los catálogos largos (municipios, unas 1.100 filas; unidades de medida, otras tantas): se piden por página
/// y con búsqueda. Los demás tienen pocas filas y salen completos.
/// </param>
public sealed record CatalogDefinition(string Name, CatalogKind Kind, bool Paged);

/// <summary>Los catálogos publicados, por su nombre en la ruta.</summary>
public static class CatalogDefinitions
{
    public static readonly IReadOnlyList<CatalogDefinition> All =
    [
        new("identification-types", CatalogKind.IdentificationTypes, Paged: false),
        new("organization-types", CatalogKind.OrganizationTypes, Paged: false),
        new("regimes", CatalogKind.Regimes, Paged: false),
        new("liabilities", CatalogKind.Liabilities, Paged: false),
        new("municipalities", CatalogKind.Municipalities, Paged: true),
        new("departments", CatalogKind.Departments, Paged: false),
        new("countries", CatalogKind.Countries, Paged: false),
        new("unit-measures", CatalogKind.UnitMeasures, Paged: true),
        new("taxes", CatalogKind.Taxes, Paged: false),
        new("payment-forms", CatalogKind.PaymentForms, Paged: false),
        new("payment-methods", CatalogKind.PaymentMethods, Paged: false),
        new("document-types", CatalogKind.DocumentTypes, Paged: false),
        new("currencies", CatalogKind.Currencies, Paged: false),
        new("item-identification-types", CatalogKind.ItemIdentificationTypes, Paged: false),
        new("credit-note-discrepancy-responses", CatalogKind.CreditNoteDiscrepancyResponses, Paged: false),
        new("debit-note-discrepancy-responses", CatalogKind.DebitNoteDiscrepancyResponses, Paged: false),
    ];

    private static readonly Dictionary<string, CatalogDefinition> ByName =
        All.ToDictionary(definition => definition.Name, StringComparer.Ordinal);

    /// <summary>El catálogo con ese nombre exacto (en minúsculas, con guiones); nulo si no se publica.</summary>
    public static CatalogDefinition? Find(string? name) => name is not null && ByName.TryGetValue(name, out var definition) ? definition : null;
}

/// <summary>Una fila de un catálogo tal como está guardada, activa o no.</summary>
/// <param name="DepartmentId">Departamento del municipio; nulo en los demás catálogos.</param>
/// <param name="CountryId">País del departamento; nulo en los demás catálogos.</param>
/// <param name="Description">Descripción del impuesto; nula en los demás catálogos.</param>
public sealed record CatalogRow(
    int Id, string Code, string Name, bool IsActive, int? DepartmentId = null, int? CountryId = null, string? Description = null);

/// <summary>Lectura de las filas de un catálogo sembrado (Infrastructure, con EF Core).</summary>
public interface ICatalogRows
{
    /// <summary>Todas las filas del catálogo, activas e inactivas, en orden de id.</summary>
    Task<IReadOnlyList<CatalogRow>> ListAsync(CatalogKind kind, CancellationToken ct);
}
