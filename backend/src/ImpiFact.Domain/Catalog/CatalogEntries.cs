namespace ImpiFact.Domain.Catalog;

// Catálogos DIAN de la paridad Community (Modelo de datos detallado, sección 3.1). Cada clase es una tabla con
// el nombre del PHP; las que no declaran nada tienen solo la forma base de CatalogEntry.

/// <summary>Tipos de documento (<c>type_documents</c>): factura, notas, nómina, documento soporte y demás.</summary>
public sealed class TypeDocument : CatalogEntry
{
    /// <summary>Algoritmo del código único del documento, por ejemplo <c>CUFE-SHA384</c>; nulo en el id 6 (ZIP).</summary>
    public string? CufeAlgorithm { get; init; }

    /// <summary>Prefijo de los archivos del PHP (<c>fv</c>, <c>nc</c>…), no el prefijo de la resolución.</summary>
    public string? Prefix { get; init; }
}

/// <summary>Tributos (<c>taxes</c>): IVA, INC, ICA, retenciones y demás.</summary>
public sealed class Tax : CatalogEntry
{
    public required string Description { get; init; }
}

/// <summary>Tipos de documento de identificación (<c>type_document_identifications</c>): NIT, cédula y demás.</summary>
public sealed class TypeDocumentIdentification : CatalogEntry
{
    /// <summary>Código RIPS (registro de prestación de servicios de salud) del mismo tipo de documento.</summary>
    public string? RipsCode { get; init; }
}

/// <summary>Estándares de identificación de artículos (<c>type_item_identifications</c>): UNSPSC, GTIN y demás.</summary>
public sealed class TypeItemIdentification : CatalogEntry
{
    /// <summary>Agencia del estándar; el XML lo escribe como <c>schemeAgencyID</c>.</summary>
    public string? CodeAgency { get; init; }
}

/// <summary>Países (<c>countries</c>) con su código ISO 3166 de dos letras.</summary>
public sealed class Country : CatalogEntry;

/// <summary>Departamentos de Colombia (<c>departments</c>).</summary>
public sealed class Department : CatalogEntry
{
    public int CountryId { get; init; }
}

/// <summary>Municipios de Colombia (<c>municipalities</c>) con su código DANE (Departamento Administrativo Nacional de Estadística).</summary>
public sealed class Municipality : CatalogEntry
{
    public int DepartmentId { get; init; }

    /// <summary>Código del municipio en el facturador gratuito de la DIAN; se carga el valor final del PHP.</summary>
    public long? Codefacturador { get; init; }
}

/// <summary>Regímenes fiscales (<c>type_regimes</c>).</summary>
public sealed class TypeRegime : CatalogEntry;

/// <summary>Tipos de organización jurídica (<c>type_organizations</c>): persona jurídica o natural.</summary>
public sealed class TypeOrganization : CatalogEntry;

/// <summary>Responsabilidades fiscales (<c>type_liabilities</c>), como gran contribuyente o autorretenedor.</summary>
public sealed class TypeLiability : CatalogEntry;

/// <summary>Ambientes de la DIAN (<c>type_environments</c>): id 1 producción, id 2 habilitación (pruebas).</summary>
public sealed class TypeEnvironment : CatalogEntry
{
    public const int Production = 1;

    public const int Habilitation = 2;
}

/// <summary>Monedas (<c>type_currencies</c>) con su código ISO 4217.</summary>
public sealed class TypeCurrency : CatalogEntry;

/// <summary>Unidades de medida (<c>unit_measures</c>); el PHP repite algunos códigos.</summary>
public sealed class UnitMeasure : CatalogEntry;

/// <summary>Tipos de operación (<c>type_operations</c>); los códigos 10 y 11 se repiten en el documento soporte.</summary>
public sealed class TypeOperation : CatalogEntry;

/// <summary>Tipos de descuento (<c>type_discounts</c>).</summary>
public sealed class TypeDiscount : CatalogEntry;

/// <summary>Códigos de descuento (<c>discounts</c>).</summary>
public sealed class Discount : CatalogEntry;

/// <summary>Precios de referencia de las muestras gratis (<c>reference_prices</c>).</summary>
public sealed class ReferencePrice : CatalogEntry;

/// <summary>Formas de pago (<c>payment_forms</c>): contado o crédito.</summary>
public sealed class PaymentForm : CatalogEntry;

/// <summary>Medios de pago (<c>payment_methods</c>).</summary>
public sealed class PaymentMethod : CatalogEntry;

/// <summary>Motivos de rechazo de una factura (<c>type_rejections</c>).</summary>
public sealed class TypeRejection : CatalogEntry;

/// <summary>Eventos RADIAN (<c>events</c>): acuse de recibo, reclamo, aceptación y demás.</summary>
public sealed class Event : CatalogEntry;

/// <summary>Términos de entrega internacional (<c>incoterms</c>).</summary>
public sealed class Incoterm : CatalogEntry;

/// <summary>Tipos de pago anticipado del sector salud (<c>prepaid_payment_types</c>).</summary>
public sealed class PrepaidPaymentType : CatalogEntry;

/// <summary>Formas de generación y transmisión del documento soporte (<c>type_generation_transmitions</c>).</summary>
public sealed class TypeGenerationTransmition : CatalogEntry;

/// <summary>Tipos de servicios públicos domiciliarios (<c>type_spds</c>).</summary>
public sealed class TypeSpd : CatalogEntry;

/// <summary>Idiomas (<c>languages</c>) con su código ISO 639.</summary>
public sealed class Language : CatalogEntry;

/// <summary>Conceptos de corrección de la nota crédito (<c>credit_note_discrepancy_responses</c>).</summary>
public sealed class CreditNoteDiscrepancyResponse : CatalogEntry;

/// <summary>Conceptos de corrección de la nota débito (<c>debit_note_discrepancy_responses</c>).</summary>
public sealed class DebitNoteDiscrepancyResponse : CatalogEntry;

/// <summary>Conceptos de corrección de la nota de ajuste del documento soporte (<c>credit_note_discrepancy_responses_sd</c>).</summary>
public sealed class CreditNoteDiscrepancyResponseSd : CatalogEntry;
