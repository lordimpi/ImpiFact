using System.Text.Json.Nodes;
using ImpiFact.Domain.Common;

namespace ImpiFact.Domain.Documents;

// Documentos (Modelo de datos detallado, sección 3.5). La cabecera reemplaza las 77 columnas planas del PHP y
// cada parte va en su tabla hija. Un documento fiscal no se borra (supuesto A7): no hay borrado lógico.

/// <summary>
/// Documento electrónico emitido (<c>documents</c>): factura, nota, nómina, documento soporte o equivalente.
/// El prefijo vacío reemplaza al nulo para que el único de numeración funcione igual en todos los motores.
/// </summary>
public sealed class Document : TenantEntity
{
    // EF Core (Entity Framework, el mapeador de objetos a tablas) lee y escribe este campo directamente al cargar
    // la fila; el código del dominio lo cambia solo desde MoveTo.
    private DocumentStatus _status = DocumentStatus.Received;

    public int DocumentTypeId { get; init; }

    /// <summary>Ambiente DIAN en que se emitió.</summary>
    public int TypeEnvironmentId { get; init; }

    /// <summary>Resolución de numeración; nula para nómina o documento soporte sin resolución.</summary>
    public Guid? ResolutionId { get; init; }

    public string Prefix { get; init; } = "";

    public long Number { get; init; }

    /// <summary>Prefijo y número juntos, como se muestran.</summary>
    public required string FullNumber { get; init; }

    /// <summary>
    /// Estado del documento. Un documento nuevo empieza en <see cref="DocumentStatus.Received"/>; después solo
    /// cambia con <see cref="MoveTo"/>, que aplica las transiciones permitidas. El valor inicial se puede fijar al
    /// crear el objeto (por ejemplo, al cargar datos de prueba), pero nunca después.
    /// </summary>
    public DocumentStatus Status
    {
        get => _status;
        init => _status = Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(Status), value, "Unknown document status.");
    }

    /// <summary>CUFE, CUDE o CUNE (códigos únicos de factura, documento y nómina) en hexadecimal minúsculo.</summary>
    public string? Cufe { get; set; }

    /// <summary>Algoritmo del código único: <c>CUFE-SHA384</c>, <c>CUDE-SHA384</c> o <c>CUDS-SHA384</c>.</summary>
    public string? UuidAlgorithm { get; set; }

    /// <summary>Fecha de emisión en hora Colombia (UTC-5), como va en el XML.</summary>
    public DateOnly IssueDate { get; init; }

    /// <summary>Hora de emisión en hora Colombia, como va en el XML.</summary>
    public TimeOnly IssueTime { get; init; }

    /// <summary>El mismo instante de emisión en UTC, para consultar y ordenar.</summary>
    public DateTimeOffset IssuedAt { get; init; }

    public Guid? CustomerId { get; init; }

    /// <summary>Copia congelada del cliente: el XML depende de estos datos, no del cliente vivo.</summary>
    public required CustomerSnapshot CustomerSnapshot { get; init; }

    public int CurrencyId { get; init; }

    public decimal? ExchangeRate { get; init; }

    public int? TypeOperationId { get; init; }

    public decimal LineExtensionAmount { get; init; }

    public decimal TaxExclusiveAmount { get; init; }

    public decimal TaxInclusiveAmount { get; init; }

    public decimal AllowanceTotalAmount { get; init; }

    public decimal ChargeTotalAmount { get; init; }

    public decimal PrepaidAmount { get; init; }

    public decimal PayableAmount { get; init; }

    /// <summary>Documento original al que se refiere una nota.</summary>
    public Guid? ReferenceDocumentId { get; init; }

    /// <summary>Código del concepto de corrección de la nota (1, 2, 3…).</summary>
    public int? DiscrepancyCode { get; init; }

    public string? DiscrepancyDescription { get; init; }

    public string? Notes { get; init; }

    public string? HeadNote { get; init; }

    public string? FootNote { get; init; }

    /// <summary>Identificador del envío que devuelve la DIAN.</summary>
    public string? TrackId { get; set; }

    public string? DianStatusCode { get; set; }

    public string? DianStatusDescription { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }

    public EmailStatus EmailStatus { get; set; }

    public DateTimeOffset? EmailSentAt { get; set; }

    public int? PdfTemplateId { get; init; }

    /// <summary>Datos de exportación, AIU, mandato, transporte o contingencia, que no se consultan por campo.</summary>
    public JsonObject? Extensions { get; init; }

    public ActorKind CreatedByType { get; init; }

    /// <summary>Usuario o API key que lo creó.</summary>
    public Guid? CreatedById { get; init; }

    /// <summary>
    /// Pasa el documento al estado indicado si la transición está permitida (Modelo de datos detallado, sección 4).
    /// </summary>
    /// <exception cref="InvalidDocumentTransitionException">Si la transición no está en la tabla; el estado no cambia.</exception>
    public void MoveTo(DocumentStatus next)
    {
        if (!DocumentStatusTransitions.IsAllowed(_status, next))
        {
            throw new InvalidDocumentTransitionException(_status, next);
        }

        _status = next;
    }
}

/// <summary>Datos del adquiriente copiados al emitir el documento (columna JSON <c>customer_snapshot</c>).</summary>
public sealed class CustomerSnapshot
{
    public int TypeDocumentIdentificationId { get; init; }

    public required string IdentificationNumber { get; init; }

    public string? Dv { get; init; }

    public required string Name { get; init; }

    public string? Phone { get; init; }

    public string? Address { get; init; }

    public string? Email { get; init; }

    public int? MunicipalityId { get; init; }

    public int? TypeOrganizationId { get; init; }

    public int? TypeRegimeId { get; init; }

    public int? TypeLiabilityId { get; init; }

    public string? MerchantRegistration { get; init; }
}

/// <summary>Línea del documento (<c>document_lines</c>).</summary>
public sealed class DocumentLine : TenantEntity
{
    public Guid DocumentId { get; init; }

    public int LineNumber { get; init; }

    public string? Code { get; init; }

    public required string Description { get; init; }

    public string? Notes { get; init; }

    public int TypeItemIdentificationId { get; init; }

    public int UnitMeasureId { get; init; }

    public decimal Quantity { get; init; }

    public decimal BaseQuantity { get; init; }

    public decimal UnitPrice { get; init; }

    public decimal LineExtensionAmount { get; init; }

    /// <summary>Muestra gratis: la línea va en cero con un precio de referencia.</summary>
    public bool FreeOfCharge { get; init; }

    public int? ReferencePriceId { get; init; }

    /// <summary>Datos propios de la línea (AIU, transporte, mandato), que no se consultan por campo.</summary>
    public JsonObject? Extensions { get; init; }
}

/// <summary>Total de un impuesto en la cabecera (<c>document_taxes</c>; en el XML, <c>TaxTotal</c>).</summary>
public sealed class DocumentTax : TenantEntity
{
    public Guid DocumentId { get; init; }

    public int TaxId { get; init; }

    public decimal? Percent { get; init; }

    public decimal TaxableAmount { get; init; }

    public decimal TaxAmount { get; init; }
}

/// <summary>Impuesto de una línea (<c>document_line_taxes</c>); los impuestos por unidad usan valor y unidad.</summary>
public sealed class DocumentLineTax : TenantEntity
{
    public Guid DocumentId { get; init; }

    public Guid LineId { get; init; }

    public int TaxId { get; init; }

    public decimal? Percent { get; init; }

    public decimal TaxableAmount { get; init; }

    public decimal TaxAmount { get; init; }

    public decimal? PerUnitAmount { get; init; }

    public int? UnitMeasureId { get; init; }
}

/// <summary>Descuento o cargo (<c>document_allowance_charges</c>): de la cabecera si no tiene línea.</summary>
public sealed class DocumentAllowanceCharge : TenantEntity
{
    public Guid DocumentId { get; init; }

    public Guid? LineId { get; init; }

    /// <summary>Verdadero para un cargo, falso para un descuento.</summary>
    public bool ChargeIndicator { get; init; }

    public int? DiscountId { get; init; }

    public string? Reason { get; init; }

    public decimal Amount { get; init; }

    public decimal BaseAmount { get; init; }

    public decimal? MultiplierFactor { get; init; }
}

/// <summary>Forma y medio de pago (<c>document_payments</c>).</summary>
public sealed class DocumentPayment : TenantEntity
{
    public Guid DocumentId { get; init; }

    public int PaymentFormId { get; init; }

    public int PaymentMethodId { get; init; }

    public DateOnly? DueDate { get; init; }

    public string? DurationMeasure { get; init; }
}

/// <summary>Referencia a otro documento (<c>document_references</c>): factura de origen, orden de compra y demás.</summary>
public sealed class DocumentReference : TenantEntity
{
    public Guid DocumentId { get; init; }

    public DocumentReferenceKind Kind { get; init; }

    public string? Number { get; init; }

    /// <summary>Código único del documento referido.</summary>
    public string? Uuid { get; init; }

    public DateOnly? IssueDate { get; init; }
}

/// <summary>
/// Archivo de un documento (<c>document_files</c>): XML, PDF, solicitud, respuesta de la DIAN. El contenido va al
/// almacenamiento de archivos; aquí solo la clave, el tamaño y el resumen SHA-256.
/// </summary>
public sealed class DocumentFile : TenantEntity
{
    public Guid DocumentId { get; init; }

    public DocumentFileKind Kind { get; init; }

    public int Revision { get; init; } = 1;

    public StorageProvider StorageProvider { get; init; }

    public required string StorageKey { get; init; }

    public required string ContentType { get; init; }

    public long SizeBytes { get; init; }

    /// <summary>SHA-256 del contenido en hexadecimal (64 caracteres).</summary>
    public required string Sha256 { get; init; }
}

/// <summary>Un intento contra el servicio web de la DIAN (<c>dian_submissions</c>).</summary>
public sealed class DianSubmission : TenantEntity
{
    public Guid DocumentId { get; init; }

    public int Attempt { get; init; }

    public DianOperation Operation { get; init; }

    public int TypeEnvironmentId { get; init; }

    public string? TrackId { get; set; }

    public string? ZipKey { get; set; }

    public int? HttpStatus { get; set; }

    public string? DianStatusCode { get; set; }

    public string? DianStatusDescription { get; set; }

    public bool? IsValid { get; set; }

    public List<string>? ErrorMessages { get; set; }

    public Guid? RequestFileId { get; set; }

    public Guid? ResponseFileId { get; set; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; set; }

    public int? DurationMs { get; set; }
}

/// <summary>
/// Evento RADIAN emitido o recibido (<c>document_events</c>): apunta a un documento emitido o a uno recibido,
/// nunca a los dos.
/// </summary>
/// <remarks>
/// There is one row per target and event type: sending again an event the DIAN rejected replaces the row's XML, CUDE and
/// answer (phase 7a, T6), so <see cref="Cude"/> and <see cref="IssuedAt"/> change with each new XML.
/// </remarks>
public sealed class DocumentEvent : TenantEntity
{
    public Guid? DocumentId { get; init; }

    public Guid? ReceivedDocumentId { get; init; }

    public int EventId { get; init; }

    /// <summary>CUDE del evento (código único de documento electrónico).</summary>
    public required string Cude { get; set; }

    /// <summary>Event number (cbc:ID): receiver NIT, document number and event code.</summary>
    public string? Number { get; set; }

    public DateTimeOffset IssuedAt { get; set; }

    public string? TrackId { get; set; }

    public DocumentEventStatus Status { get; set; }

    public string? DianStatusCode { get; set; }

    public string? DianStatusDescription { get; set; }

    /// <summary>Rules the DIAN rejected or notified (ErrorMessage of SendEventUpdateStatus).</summary>
    public List<string>? ErrorMessages { get; set; }

    /// <summary>Claim reason of a 031 (<c>type_rejections.code</c>: 01 to 04).</summary>
    public string? RejectionCode { get; set; }

    /// <summary>Storage key of the signed ApplicationResponse of the event, generated by the server.</summary>
    public string? XmlFileKey { get; set; }

    /// <summary>Storage key of the DIAN answer (the SOAP body), generated by the server.</summary>
    public string? ResponseFileKey { get; set; }

    public Guid? PayloadFileId { get; set; }
}

/// <summary>Factura recibida de un proveedor (<c>received_documents</c>).</summary>
public sealed class ReceivedDocument : TenantEntity
{
    public required string SupplierIdentificationNumber { get; init; }

    public required string SupplierDv { get; init; }

    public required string SupplierName { get; init; }

    /// <summary>cbc:AdditionalAccountID of the seller (1 legal, 2 natural person), for the receiver of the events.</summary>
    public string? SupplierOrganizationTypeCode { get; init; }

    /// <summary>listName of the seller's cbc:TaxLevelCode (48, 49), for the receiver of the events.</summary>
    public string? SupplierRegimeCode { get; init; }

    public string? SupplierEmail { get; init; }

    public int DocumentTypeId { get; init; }

    public string Prefix { get; init; } = "";

    public required string Number { get; init; }

    public required string Cufe { get; init; }

    public DateOnly IssueDate { get; init; }

    public int TypeEnvironmentId { get; init; }

    public decimal LineExtensionAmount { get; init; }

    public decimal TaxExclusiveAmount { get; init; }

    public decimal AllowanceTotalAmount { get; init; }

    public decimal TaxAmount { get; init; }

    public decimal PayableAmount { get; init; }

    public string? XmlFileKey { get; set; }

    public string? PdfFileKey { get; set; }
}

/// <summary>
/// Consecutivo (<c>numbering_sequences</c>; en el PHP, <c>sends</c>). La clave es <c>R:{resolution_id}</c> con
/// resolución, o <c>T:{document_type_id}:{ambiente}:{prefijo}:{año}</c> sin ella (ver <see cref="NumberingScope"/>).
/// </summary>
/// <remarks>
/// El número se toma con un solo UPDATE atómico en la base (Modelo de datos detallado, sección 5), no cambiando
/// esta entidad en memoria: así dos solicitudes simultáneas nunca obtienen el mismo número. En memoria solo se
/// crea la fila y se adelanta con <see cref="RaiseTo"/>, que nunca la hace bajar.
/// </remarks>
public sealed class NumberingSequence : TenantEntity
{
    // EF Core lee y escribe este campo directamente al cargar la fila.
    private long _nextNumber;

    public required string ScopeKey { get; init; }

    /// <summary>Siguiente número que entregará el consecutivo.</summary>
    public long NextNumber
    {
        get => _nextNumber;
        init => _nextNumber = value;
    }

    /// <summary>
    /// Adelanta el consecutivo hasta <paramref name="number"/> si está por debajo; si ya va más adelante, no cambia.
    /// Sirve, por ejemplo, cuando una resolución actualizada empieza su rango más arriba.
    /// </summary>
    public void RaiseTo(long number) => _nextNumber = Math.Max(_nextNumber, number);
}
