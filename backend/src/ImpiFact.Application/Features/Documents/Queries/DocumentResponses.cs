using System.Globalization;
using System.Text.Json.Serialization;
using ImpiFact.Application.Abstractions.Documents;

namespace ImpiFact.Application.Features.Documents.Queries;

// Respuestas canónicas de /api/v1/documents (anexo A.5.1). Nunca se exponen entidades (Arquitectura detallada,
// sección 4.2): estos registros se llenan desde los modelos de lectura. Los nombres JSON van en snake_case como el
// resto de la API. Los importes salen con los decimales guardados.

/// <summary>Cabecera de un documento: número, estado, fechas, adquiriente y totales.</summary>
public record DocumentSummaryResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("document_type_id")]
    public int DocumentTypeId { get; init; }

    /// <summary>
    /// Familia en palabras (<c>invoice</c>, <c>credit_note</c>, <c>debit_note</c>); nula para los demás tipos de
    /// documento.
    /// </summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    /// <summary>Ambiente de la DIAN en que se emitió (1 producción, 2 habilitación).</summary>
    [JsonPropertyName("type_environment_id")]
    public int TypeEnvironmentId { get; init; }

    [JsonPropertyName("prefix")]
    public string Prefix { get; init; } = "";

    [JsonPropertyName("number")]
    public long Number { get; init; }

    [JsonPropertyName("full_number")]
    public string FullNumber { get; init; } = "";

    /// <summary>Nombre del estado (<c>Received</c>, <c>Queued</c>, <c>Accepted</c>…; Modelo de datos detallado, sección 4).</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = "";

    /// <summary>CUFE o CUDE (códigos únicos de factura y de documento electrónico); nulo mientras no se calcula.</summary>
    [JsonPropertyName("cufe")]
    public string? Cufe { get; init; }

    /// <summary>Fecha de emisión en hora Colombia, <c>yyyy-MM-dd</c>.</summary>
    [JsonPropertyName("issue_date")]
    public string IssueDate { get; init; } = "";

    /// <summary>Hora de emisión en hora Colombia, <c>HH:mm:ss</c>.</summary>
    [JsonPropertyName("issue_time")]
    public string IssueTime { get; init; } = "";

    /// <summary>El mismo instante en UTC.</summary>
    [JsonPropertyName("issued_at")]
    public DateTimeOffset IssuedAt { get; init; }

    [JsonPropertyName("currency_id")]
    public int CurrencyId { get; init; }

    [JsonPropertyName("customer")]
    public DocumentCustomerResponse Customer { get; init; } = new();

    [JsonPropertyName("totals")]
    public DocumentTotalsResponse Totals { get; init; } = new();

    /// <summary>Envío del documento por correo al adquiriente: <c>NotSent</c>, <c>Sent</c> o <c>Failed</c>.</summary>
    [JsonPropertyName("email_status")]
    public string EmailStatus { get; init; } = "";
}

/// <summary>Documento completo: la cabecera más los datos de la nota, la DIAN, los impuestos y las líneas.</summary>
public sealed record DocumentDetailResponse : DocumentSummaryResponse
{
    /// <summary>Parte de la cabecera ya armada.</summary>
    public DocumentDetailResponse(DocumentSummaryResponse header)
        : base(header)
    {
    }

    /// <summary>Algoritmo del código único: <c>CUFE-SHA384</c>, <c>CUDE-SHA384</c>…</summary>
    [JsonPropertyName("uuid_algorithm")]
    public string? UuidAlgorithm { get; init; }

    [JsonPropertyName("resolution_id")]
    public Guid? ResolutionId { get; init; }

    /// <summary>Documento original de una nota.</summary>
    [JsonPropertyName("reference_document_id")]
    public Guid? ReferenceDocumentId { get; init; }

    [JsonPropertyName("type_operation_id")]
    public int? TypeOperationId { get; init; }

    [JsonPropertyName("exchange_rate")]
    public decimal? ExchangeRate { get; init; }

    /// <summary>Concepto de corrección de una nota (1, 2, 3…).</summary>
    [JsonPropertyName("discrepancy_code")]
    public int? DiscrepancyCode { get; init; }

    [JsonPropertyName("discrepancy_description")]
    public string? DiscrepancyDescription { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    [JsonPropertyName("head_note")]
    public string? HeadNote { get; init; }

    [JsonPropertyName("foot_note")]
    public string? FootNote { get; init; }

    /// <summary>Identificador del envío que devuelve la DIAN.</summary>
    [JsonPropertyName("track_id")]
    public string? TrackId { get; init; }

    [JsonPropertyName("dian_status_code")]
    public string? DianStatusCode { get; init; }

    [JsonPropertyName("dian_status_description")]
    public string? DianStatusDescription { get; init; }

    [JsonPropertyName("sent_at")]
    public DateTimeOffset? SentAt { get; init; }

    [JsonPropertyName("accepted_at")]
    public DateTimeOffset? AcceptedAt { get; init; }

    /// <summary>Totales por impuesto de la cabecera (en el XML, <c>TaxTotal</c>).</summary>
    [JsonPropertyName("taxes")]
    public IReadOnlyList<DocumentTaxResponse> Taxes { get; init; } = [];

    [JsonPropertyName("lines")]
    public IReadOnlyList<DocumentLineResponse> Lines { get; init; } = [];
}

/// <summary>Una página del listado. <c>next_cursor</c> es nulo en la última página.</summary>
public sealed record DocumentListResponse
{
    [JsonPropertyName("items")]
    public IReadOnlyList<DocumentSummaryResponse> Items { get; init; } = [];

    /// <summary>Texto opaco para pedir la página siguiente (<c>?cursor=</c>).</summary>
    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; init; }

    /// <summary>
    /// Cantidad de documentos que cumplen los filtros, sin importar la página; solo llega con <c>include_total=true</c>.
    /// </summary>
    [JsonPropertyName("total_count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TotalCount { get; init; }
}

/// <summary>Adquiriente tal como quedó copiado al emitir el documento.</summary>
public sealed record DocumentCustomerResponse
{
    [JsonPropertyName("type_document_identification_id")]
    public int TypeDocumentIdentificationId { get; init; }

    [JsonPropertyName("identification_number")]
    public string IdentificationNumber { get; init; } = "";

    [JsonPropertyName("dv")]
    public string? Dv { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("phone")]
    public string? Phone { get; init; }

    [JsonPropertyName("address")]
    public string? Address { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("municipality_id")]
    public int? MunicipalityId { get; init; }

    [JsonPropertyName("type_organization_id")]
    public int? TypeOrganizationId { get; init; }

    [JsonPropertyName("type_regime_id")]
    public int? TypeRegimeId { get; init; }

    [JsonPropertyName("type_liability_id")]
    public int? TypeLiabilityId { get; init; }

    [JsonPropertyName("merchant_registration")]
    public string? MerchantRegistration { get; init; }
}

/// <summary>Totales con los nombres de <c>legal_monetary_totals</c> del contrato de emisión.</summary>
public sealed record DocumentTotalsResponse
{
    [JsonPropertyName("line_extension_amount")]
    public decimal LineExtensionAmount { get; init; }

    [JsonPropertyName("tax_exclusive_amount")]
    public decimal TaxExclusiveAmount { get; init; }

    [JsonPropertyName("tax_inclusive_amount")]
    public decimal TaxInclusiveAmount { get; init; }

    [JsonPropertyName("allowance_total_amount")]
    public decimal AllowanceTotalAmount { get; init; }

    [JsonPropertyName("charge_total_amount")]
    public decimal ChargeTotalAmount { get; init; }

    [JsonPropertyName("pre_paid_amount")]
    public decimal PrepaidAmount { get; init; }

    [JsonPropertyName("payable_amount")]
    public decimal PayableAmount { get; init; }
}

/// <summary>Un impuesto: porcentual (<c>percent</c>) o por unidad (<c>per_unit_amount</c> y <c>unit_measure_id</c>).</summary>
public sealed record DocumentTaxResponse
{
    [JsonPropertyName("tax_id")]
    public int TaxId { get; init; }

    [JsonPropertyName("percent")]
    public decimal? Percent { get; init; }

    [JsonPropertyName("taxable_amount")]
    public decimal TaxableAmount { get; init; }

    [JsonPropertyName("tax_amount")]
    public decimal TaxAmount { get; init; }

    [JsonPropertyName("per_unit_amount")]
    public decimal? PerUnitAmount { get; init; }

    [JsonPropertyName("unit_measure_id")]
    public int? UnitMeasureId { get; init; }
}

public sealed record DocumentLineResponse
{
    [JsonPropertyName("line_number")]
    public int LineNumber { get; init; }

    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    [JsonPropertyName("type_item_identification_id")]
    public int TypeItemIdentificationId { get; init; }

    [JsonPropertyName("unit_measure_id")]
    public int UnitMeasureId { get; init; }

    [JsonPropertyName("invoiced_quantity")]
    public decimal Quantity { get; init; }

    [JsonPropertyName("base_quantity")]
    public decimal BaseQuantity { get; init; }

    [JsonPropertyName("price_amount")]
    public decimal UnitPrice { get; init; }

    [JsonPropertyName("line_extension_amount")]
    public decimal LineExtensionAmount { get; init; }

    /// <summary>Muestra gratis: la línea va en cero con un precio de referencia.</summary>
    [JsonPropertyName("free_of_charge_indicator")]
    public bool FreeOfCharge { get; init; }

    [JsonPropertyName("reference_price_id")]
    public int? ReferencePriceId { get; init; }

    [JsonPropertyName("tax_totals")]
    public IReadOnlyList<DocumentTaxResponse> Taxes { get; init; } = [];
}

/// <summary>Copia los modelos de lectura a las respuestas canónicas.</summary>
internal static class DocumentMapping
{
    public static DocumentDetailResponse Detail(DocumentDetail detail) => new(Summary(detail.Header))
    {
        UuidAlgorithm = detail.UuidAlgorithm,
        ResolutionId = detail.ResolutionId,
        ReferenceDocumentId = detail.ReferenceDocumentId,
        TypeOperationId = detail.TypeOperationId,
        ExchangeRate = detail.ExchangeRate,
        DiscrepancyCode = detail.DiscrepancyCode,
        DiscrepancyDescription = detail.DiscrepancyDescription,
        Notes = detail.Notes,
        HeadNote = detail.HeadNote,
        FootNote = detail.FootNote,
        TrackId = detail.TrackId,
        DianStatusCode = detail.DianStatusCode,
        DianStatusDescription = detail.DianStatusDescription,
        SentAt = detail.SentAt,
        AcceptedAt = detail.AcceptedAt,
        Taxes = detail.Taxes.Select(Tax).ToList(),
        Lines = detail.Lines.Select(Line).ToList(),
    };

    public static DocumentSummaryResponse Summary(DocumentSummary document) => new()
    {
        Id = document.Id,
        DocumentTypeId = document.DocumentTypeId,
        Kind = DocumentKinds.NameOf(document.DocumentTypeId),
        TypeEnvironmentId = document.TypeEnvironmentId,
        Prefix = document.Prefix,
        Number = document.Number,
        FullNumber = document.FullNumber,
        Status = document.Status.ToString(),
        Cufe = document.Cufe,
        IssueDate = document.IssueDate.ToString(ColombiaDates.Format, CultureInfo.InvariantCulture),
        IssueTime = document.IssueTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
        IssuedAt = document.IssuedAt,
        CurrencyId = document.CurrencyId,
        Customer = new DocumentCustomerResponse
        {
            TypeDocumentIdentificationId = document.Customer.TypeDocumentIdentificationId,
            IdentificationNumber = document.Customer.IdentificationNumber,
            Dv = document.Customer.Dv,
            Name = document.Customer.Name,
            Phone = document.Customer.Phone,
            Address = document.Customer.Address,
            Email = document.Customer.Email,
            MunicipalityId = document.Customer.MunicipalityId,
            TypeOrganizationId = document.Customer.TypeOrganizationId,
            TypeRegimeId = document.Customer.TypeRegimeId,
            TypeLiabilityId = document.Customer.TypeLiabilityId,
            MerchantRegistration = document.Customer.MerchantRegistration,
        },
        Totals = new DocumentTotalsResponse
        {
            LineExtensionAmount = document.Totals.LineExtensionAmount,
            TaxExclusiveAmount = document.Totals.TaxExclusiveAmount,
            TaxInclusiveAmount = document.Totals.TaxInclusiveAmount,
            AllowanceTotalAmount = document.Totals.AllowanceTotalAmount,
            ChargeTotalAmount = document.Totals.ChargeTotalAmount,
            PrepaidAmount = document.Totals.PrepaidAmount,
            PayableAmount = document.Totals.PayableAmount,
        },
        EmailStatus = document.EmailStatus.ToString(),
    };

    private static DocumentTaxResponse Tax(DocumentTaxAmount tax) => new()
    {
        TaxId = tax.TaxId,
        Percent = tax.Percent,
        TaxableAmount = tax.TaxableAmount,
        TaxAmount = tax.TaxAmount,
        PerUnitAmount = tax.PerUnitAmount,
        UnitMeasureId = tax.UnitMeasureId,
    };

    private static DocumentLineResponse Line(DocumentLineDetail line) => new()
    {
        LineNumber = line.LineNumber,
        Code = line.Code,
        Description = line.Description,
        Notes = line.Notes,
        TypeItemIdentificationId = line.TypeItemIdentificationId,
        UnitMeasureId = line.UnitMeasureId,
        Quantity = line.Quantity,
        BaseQuantity = line.BaseQuantity,
        UnitPrice = line.UnitPrice,
        LineExtensionAmount = line.LineExtensionAmount,
        FreeOfCharge = line.FreeOfCharge,
        ReferencePriceId = line.ReferencePriceId,
        Taxes = line.Taxes.Select(Tax).ToList(),
    };
}
