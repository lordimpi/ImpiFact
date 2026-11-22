namespace ImpiFact.Application.Abstractions.Documents;

/// <summary>
/// Catálogos DIAN (las listas de códigos de la Dirección de Impuestos y Aduanas Nacionales) que usa la emisión de
/// documentos: traducen el id que envía el contrato JSON (<c>tax_id: 1</c>) al código y al nombre que van en el XML
/// (<c>01</c>, <c>IVA</c>). Los catálogos se siembran y no cambian durante la ejecución.
/// </summary>
public interface IDocumentCatalogs
{
    /// <summary>
    /// Busca de una vez todas las filas pedidas. Las que no existen no aparecen en el resultado, así el caso de uso
    /// las informa como datos inválidos (la regla <c>exists</c> del PHP).
    /// </summary>
    Task<IReadOnlyDictionary<CatalogKey, CatalogItem>> FindAsync(IReadOnlyCollection<CatalogKey> keys, CancellationToken ct);
}

/// <summary>Catálogo de la DIAN, con el nombre de su tabla en el comentario.</summary>
public enum DianCatalog
{
    /// <summary><c>type_documents</c>.</summary>
    DocumentType,

    /// <summary><c>type_operations</c>.</summary>
    OperationType,

    /// <summary><c>type_currencies</c>.</summary>
    Currency,

    /// <summary><c>type_organizations</c>.</summary>
    OrganizationType,

    /// <summary><c>type_document_identifications</c>.</summary>
    IdentificationType,

    /// <summary><c>type_regimes</c>.</summary>
    Regime,

    /// <summary><c>type_liabilities</c>.</summary>
    Liability,

    /// <summary><c>taxes</c>.</summary>
    Tax,

    /// <summary><c>municipalities</c>, con su departamento en <see cref="CatalogItem.Department"/>.</summary>
    Municipality,

    /// <summary><c>countries</c>.</summary>
    Country,

    /// <summary><c>languages</c>.</summary>
    Language,

    /// <summary><c>payment_forms</c>.</summary>
    PaymentForm,

    /// <summary><c>payment_methods</c>.</summary>
    PaymentMethod,

    /// <summary><c>discounts</c>.</summary>
    Discount,

    /// <summary><c>unit_measures</c>.</summary>
    UnitMeasure,

    /// <summary><c>type_item_identifications</c>, con su agencia en <see cref="CatalogItem.CodeAgency"/>.</summary>
    ItemIdentificationType,

    /// <summary><c>reference_prices</c>.</summary>
    ReferencePrice,

    /// <summary><c>credit_note_discrepancy_responses</c>.</summary>
    CreditNoteDiscrepancy,

    /// <summary><c>debit_note_discrepancy_responses</c>.</summary>
    DebitNoteDiscrepancy,

    /// <summary><c>credit_note_discrepancy_responses_sd</c>: reasons of a support-document adjustment note (1 to 5).</summary>
    SupportDocumentDiscrepancy,

    /// <summary><c>type_generation_transmitions</c>: how a support-document line is reported (per operation, weekly).</summary>
    GenerationTransmition,

    /// <summary><c>prepaid_payment_types</c>: collection concepts of a health-sector prepaid payment (copago, cuota moderadora…).</summary>
    PrepaidPaymentType,
}

/// <summary>Una fila pedida: el catálogo y el id.</summary>
public readonly record struct CatalogKey(DianCatalog Catalog, int Id);

/// <summary>Fila de un catálogo con lo que necesita el XML.</summary>
/// <param name="Code">Código DIAN, tal como está guardado.</param>
/// <param name="Name">Nombre, tal como está guardado (con sus espacios).</param>
/// <param name="CodeAgency">Agencia del esquema de identificación del artículo; solo en <see cref="DianCatalog.ItemIdentificationType"/>.</param>
/// <param name="Department">Departamento del municipio; solo en <see cref="DianCatalog.Municipality"/>.</param>
public sealed record CatalogItem(string Code, string Name, string? CodeAgency = null, CatalogItem? Department = null);
