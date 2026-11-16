namespace ImpiFact.Domain.Catalog;

/// <summary>
/// Fila de un catálogo DIAN (las listas de códigos del anexo técnico de la Dirección de Impuestos y Aduanas
/// Nacionales; Modelo de datos detallado, sección 3.1). El <see cref="Id"/> es el mismo del PHP y no se genera:
/// el contrato JSON envía esos números (<c>type_document_id: 1</c>, <c>tax_id: 1</c>). Los catálogos se siembran
/// en la migración y no se editan: un cambio de anexo entra como fila nueva con <see cref="ValidFrom"/>.
/// </summary>
public abstract class CatalogEntry
{
    public int Id { get; init; }

    /// <summary>Código DIAN.</summary>
    public required string Code { get; init; }

    public required string Name { get; init; }

    public bool IsActive { get; init; } = true;

    /// <summary>Inicio de la vigencia DIAN; nulo en las filas heredadas del PHP.</summary>
    public DateOnly? ValidFrom { get; init; }

    public DateOnly? ValidTo { get; init; }
}
