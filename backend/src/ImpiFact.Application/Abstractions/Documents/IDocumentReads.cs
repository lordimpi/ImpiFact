using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Abstractions.Documents;

/// <summary>
/// Consultas de solo lectura del módulo Documents (Arquitectura detallada, sección 4.1). Las implementa
/// Infrastructure con Dapper, una librería que ejecuta SQL escrito a mano y llena objetos; no rastrean cambios ni
/// abren transacción. Toda consulta recibe la empresa y la pone en el WHERE: los filtros globales de EF Core
/// (Entity Framework, el mapeador de objetos a tablas) no aplican a estas lecturas.
/// </summary>
public interface IDocumentReads
{
    /// <summary>NIT y razón social de la empresa; nulo si la empresa no existe.</summary>
    Task<DocumentCompany?> FindCompanyAsync(Guid companyId, CancellationToken ct);

    /// <summary>
    /// Documentos de la empresa del tipo y los estados indicados, del más antiguo al más reciente. Sin
    /// <paramref name="range"/> no filtra por fecha; con <paramref name="page"/> devuelve solo esa página.
    /// </summary>
    Task<IReadOnlyList<DocumentSummary>> ListByTypeAsync(
        Guid companyId, int documentTypeId, IReadOnlyCollection<DocumentStatus> statuses, IssuedRange? range, PageWindow? page, CancellationToken ct);

    /// <summary>Cantidad de documentos por tipo; los tipos sin documentos no aparecen.</summary>
    Task<IReadOnlyDictionary<int, long>> CountByTypeAsync(
        Guid companyId, IReadOnlyCollection<int> documentTypeIds, IReadOnlyCollection<DocumentStatus> statuses, IssuedRange range, CancellationToken ct);

    /// <summary>
    /// Listado del más reciente al más antiguo, por clave <c>(issued_at, id)</c> y no por desplazamiento: la página
    /// siguiente empieza justo después de <see cref="DocumentListFilter.After"/>.
    /// </summary>
    Task<IReadOnlyList<DocumentSummary>> ListAsync(DocumentListFilter filter, CancellationToken ct);

    /// <summary>
    /// Cantidad de documentos que cumplen los filtros, sin paginar: no usa <see cref="DocumentListFilter.After"/> ni
    /// <see cref="DocumentListFilter.Take"/>. Recorre todas las filas que cumplen los filtros, así que cuesta más que
    /// leer una página.
    /// </summary>
    Task<long> CountAsync(DocumentListFilter filter, CancellationToken ct);

    /// <summary>
    /// Los mismos documentos de <see cref="ListAsync"/>, en el mismo orden y hasta <see cref="DocumentListFilter.Take"/>
    /// filas, leídos de a uno mientras se recorren: la conexión queda abierta hasta terminar el recorrido y nunca se
    /// cargan todos en memoria. Lo usa la exportación CSV.
    /// </summary>
    IAsyncEnumerable<DocumentSummary> StreamAsync(DocumentListFilter filter, CancellationToken ct);

    /// <summary>Documento completo por su id; nulo si no existe en la empresa.</summary>
    Task<DocumentDetail?> FindByIdAsync(Guid companyId, Guid documentId, CancellationToken ct);

    /// <summary>Documento completo por su CUFE (código único de factura electrónica); nulo si no existe en la empresa.</summary>
    Task<DocumentDetail?> FindByCufeAsync(Guid companyId, string cufe, CancellationToken ct);

    /// <summary>Próximo número de un consecutivo sin tomarlo; nulo si el consecutivo no existe en la empresa.</summary>
    Task<long?> PeekNextNumberAsync(Guid companyId, string scopeKey, CancellationToken ct);
}

/// <summary>Empresa dueña de los documentos.</summary>
/// <param name="IdentificationNumber">NIT (Número de Identificación Tributaria) sin dígito de verificación.</param>
public sealed record DocumentCompany(Guid Id, string IdentificationNumber, string Name);

/// <summary>Instantes de emisión en UTC: desde <see cref="From"/> incluido hasta <see cref="Until"/> excluido.</summary>
public readonly record struct IssuedRange(DateTimeOffset From, DateTimeOffset Until);

/// <summary>Página de un listado heredado por número de página: salta <see cref="Skip"/> filas y toma <see cref="Take"/>.</summary>
public readonly record struct PageWindow(int Skip, int Take);

/// <summary>Posición de un documento en el orden del listado: instante de emisión y id.</summary>
public readonly record struct DocumentKey(DateTimeOffset IssuedAt, Guid Id);

/// <summary>Filtros del listado canónico. Los nulos y la lista de estados vacía no filtran.</summary>
/// <param name="Statuses">Estados aceptados (basta uno); vacía para todos.</param>
/// <param name="IssuedFrom">Primer instante de emisión incluido (UTC).</param>
/// <param name="IssuedUntil">Primer instante de emisión que ya no se incluye (UTC).</param>
/// <param name="After">Último documento de la página anterior; nulo para la primera página.</param>
/// <param name="Take">Máximo de filas que devuelve la consulta.</param>
public sealed record DocumentListFilter(
    Guid CompanyId,
    int? DocumentTypeId,
    IReadOnlyCollection<DocumentStatus> Statuses,
    DateTimeOffset? IssuedFrom,
    DateTimeOffset? IssuedUntil,
    DocumentKey? After,
    int Take)
{
    /// <summary>
    /// Texto que debe aparecer, sin distinguir mayúsculas, en el número de identificación o en el nombre del
    /// adquiriente copiado en el documento.
    /// </summary>
    public string? Customer { get; init; }

    /// <summary>
    /// Número del documento: el número completo con prefijo (<c>SETP990000001</c>, sin distinguir mayúsculas) o solo
    /// los dígitos, que coinciden con ese número en cualquier prefijo.
    /// </summary>
    public string? Number { get; init; }
}

/// <summary>Adquiriente tal como quedó copiado en el documento al emitirlo.</summary>
public sealed record DocumentCustomer(
    int TypeDocumentIdentificationId,
    string IdentificationNumber,
    string? Dv,
    string Name,
    string? Phone,
    string? Address,
    string? Email,
    int? MunicipalityId,
    int? TypeOrganizationId,
    int? TypeRegimeId,
    int? TypeLiabilityId,
    string? MerchantRegistration);

/// <summary>Totales monetarios de la cabecera (en el XML, <c>LegalMonetaryTotal</c>).</summary>
public sealed record DocumentTotals(
    decimal LineExtensionAmount,
    decimal TaxExclusiveAmount,
    decimal TaxInclusiveAmount,
    decimal AllowanceTotalAmount,
    decimal ChargeTotalAmount,
    decimal PrepaidAmount,
    decimal PayableAmount);

/// <summary>Cabecera de un documento para los listados.</summary>
/// <param name="IssueDate">Fecha de emisión en hora Colombia, como va en el XML.</param>
/// <param name="IssueTime">Hora de emisión en hora Colombia.</param>
/// <param name="IssuedAt">El mismo instante en UTC.</param>
/// <param name="EmailStatus">Estado del envío del documento por correo al adquiriente.</param>
public sealed record DocumentSummary(
    Guid Id,
    int DocumentTypeId,
    int TypeEnvironmentId,
    string Prefix,
    long Number,
    string FullNumber,
    DocumentStatus Status,
    string? Cufe,
    DateOnly IssueDate,
    TimeOnly IssueTime,
    DateTimeOffset IssuedAt,
    int CurrencyId,
    DocumentCustomer Customer,
    DocumentTotals Totals,
    EmailStatus EmailStatus = EmailStatus.NotSent);

/// <summary>Documento completo: cabecera, datos de la DIAN, impuestos y líneas.</summary>
public sealed record DocumentDetail(
    DocumentSummary Header,
    string? UuidAlgorithm,
    Guid? ResolutionId,
    Guid? ReferenceDocumentId,
    int? TypeOperationId,
    decimal? ExchangeRate,
    int? DiscrepancyCode,
    string? DiscrepancyDescription,
    string? Notes,
    string? HeadNote,
    string? FootNote,
    string? TrackId,
    string? DianStatusCode,
    string? DianStatusDescription,
    DateTimeOffset? SentAt,
    DateTimeOffset? AcceptedAt,
    IReadOnlyList<DocumentTaxAmount> Taxes,
    IReadOnlyList<DocumentLineDetail> Lines);

/// <summary>Un impuesto con su base y su valor, de la cabecera o de una línea.</summary>
/// <param name="PerUnitAmount">Valor por unidad de los impuestos por unidad; nulo en los porcentuales.</param>
public sealed record DocumentTaxAmount(
    int TaxId,
    decimal? Percent,
    decimal TaxableAmount,
    decimal TaxAmount,
    decimal? PerUnitAmount,
    int? UnitMeasureId);

/// <summary>Línea del documento con sus impuestos.</summary>
public sealed record DocumentLineDetail(
    int LineNumber,
    string? Code,
    string Description,
    string? Notes,
    int TypeItemIdentificationId,
    int UnitMeasureId,
    decimal Quantity,
    decimal BaseQuantity,
    decimal UnitPrice,
    decimal LineExtensionAmount,
    bool FreeOfCharge,
    int? ReferencePriceId,
    IReadOnlyList<DocumentTaxAmount> Taxes);
