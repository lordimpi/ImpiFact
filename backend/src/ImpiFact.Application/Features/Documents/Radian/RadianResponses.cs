using System.Text.Json.Serialization;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Features.Documents.Radian;

/// <summary>A RADIAN event registered (or attempted) on a received or an own invoice.</summary>
public sealed record DocumentEventResponse
{
    [JsonPropertyName("id")]
    public required Guid Id { get; init; }

    /// <summary>DIAN code of the event: 030 to 034.</summary>
    [JsonPropertyName("event_code")]
    public required string EventCode { get; init; }

    [JsonPropertyName("event_name")]
    public required string EventName { get; init; }

    /// <summary><c>accepted</c> (registered by the DIAN), <c>rejected</c> or <c>unconfirmed</c> (sent without an answer).</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>CUDE of the event.</summary>
    [JsonPropertyName("cude")]
    public required string Cude { get; init; }

    /// <summary>Event number (cbc:ID).</summary>
    [JsonPropertyName("number")]
    public string? Number { get; init; }

    [JsonPropertyName("issued_at")]
    public required DateTimeOffset IssuedAt { get; init; }

    /// <summary>Claim reason of a 031: 01 to 04.</summary>
    [JsonPropertyName("claim_reason")]
    public string? ClaimReason { get; init; }

    [JsonPropertyName("dian_status_code")]
    public string? DianStatusCode { get; init; }

    [JsonPropertyName("dian_status_description")]
    public string? DianStatusDescription { get; init; }

    /// <summary>Rules the DIAN rejected or notified.</summary>
    [JsonPropertyName("error_messages")]
    public IReadOnlyList<string> ErrorMessages { get; init; } = [];
}

/// <summary>An invoice the company received from a seller, with its events.</summary>
public sealed record ReceivedDocumentResponse
{
    [JsonPropertyName("id")]
    public required Guid Id { get; init; }

    /// <summary>NIT of the seller, without check digit.</summary>
    [JsonPropertyName("supplier_identification_number")]
    public required string SupplierIdentificationNumber { get; init; }

    [JsonPropertyName("supplier_dv")]
    public required string SupplierDv { get; init; }

    [JsonPropertyName("supplier_name")]
    public required string SupplierName { get; init; }

    [JsonPropertyName("supplier_email")]
    public string? SupplierEmail { get; init; }

    /// <summary><c>type_documents</c> id: 1 national invoice, 2 export, 3 contingency, 12 type 04.</summary>
    [JsonPropertyName("type_document_id")]
    public required int TypeDocumentId { get; init; }

    [JsonPropertyName("prefix")]
    public required string Prefix { get; init; }

    /// <summary>Number of the invoice with its prefix, as in its cbc:ID.</summary>
    [JsonPropertyName("number")]
    public required string Number { get; init; }

    [JsonPropertyName("cufe")]
    public required string Cufe { get; init; }

    [JsonPropertyName("issue_date")]
    public required DateOnly IssueDate { get; init; }

    /// <summary>Environment of the invoice (its ProfileExecutionID): 1 production, 2 habilitación.</summary>
    [JsonPropertyName("type_environment_id")]
    public required int TypeEnvironmentId { get; init; }

    [JsonPropertyName("line_extension_amount")]
    public required decimal LineExtensionAmount { get; init; }

    [JsonPropertyName("tax_exclusive_amount")]
    public required decimal TaxExclusiveAmount { get; init; }

    [JsonPropertyName("allowance_total_amount")]
    public required decimal AllowanceTotalAmount { get; init; }

    /// <summary>Tax inclusive amount minus the line extension amount, as the PHP stores it.</summary>
    [JsonPropertyName("tax_amount")]
    public required decimal TaxAmount { get; init; }

    [JsonPropertyName("payable_amount")]
    public required decimal PayableAmount { get; init; }

    [JsonPropertyName("created_at")]
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Events of the invoice, oldest first.</summary>
    [JsonPropertyName("events")]
    public IReadOnlyList<DocumentEventResponse> Events { get; init; } = [];
}

/// <summary>A page of received documents.</summary>
public sealed record ReceivedDocumentListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<ReceivedDocumentResponse> Data { get; init; }

    /// <summary>Documents that match the filters, without paging.</summary>
    [JsonPropertyName("total_count")]
    public required long TotalCount { get; init; }

    [JsonPropertyName("limit")]
    public required int Limit { get; init; }

    [JsonPropertyName("offset")]
    public required int Offset { get; init; }
}

/// <summary>The events of an own invoice: those sent by the company and, when asked, those the DIAN has registered.</summary>
public sealed record DocumentEventListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<DocumentEventResponse> Data { get; init; }
}

/// <summary>An event the DIAN has registered on an invoice (GetStatusEvent), with the fields of the PHP (php-rules 16.6, item 6).</summary>
public sealed record DianEventResponse
{
    [JsonPropertyName("event_number")]
    public string? EventNumber { get; init; }

    [JsonPropertyName("dian_code")]
    public required string DianCode { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("date")]
    public DateOnly? Date { get; init; }

    [JsonPropertyName("time")]
    public TimeOnly? Time { get; init; }

    [JsonPropertyName("cude")]
    public string? Cude { get; init; }
}

/// <summary>The events the DIAN has registered on an invoice of the company (own or received).</summary>
public sealed record DianEventListResponse
{
    /// <summary>True when the DIAN answered the query (even with no events).</summary>
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("cufe")]
    public required string Cufe { get; init; }

    [JsonPropertyName("events")]
    public required IReadOnlyList<DianEventResponse> Events { get; init; }
}

/// <summary>
/// Result of sending an event. <c>success</c> is true only when the DIAN registered it (the PHP answers true whenever the
/// DIAN answers, php-rules 16.2, item 8). The <c>invoice_*</c>, <c>transmitter_*</c> and <c>receiver_*</c> fields are those
/// of the PHP's <c>send-event</c> answer, filled when the event is accepted.
/// </summary>
public sealed record RadianEventResponse
{
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("cude")]
    public required string Cude { get; init; }

    [JsonPropertyName("event")]
    public required DocumentEventResponse Event { get; init; }

    [JsonPropertyName("received_document_id")]
    public Guid? ReceivedDocumentId { get; init; }

    [JsonPropertyName("document_id")]
    public Guid? DocumentId { get; init; }

    [JsonPropertyName("transmitter_id")]
    public string? TransmitterId { get; init; }

    [JsonPropertyName("transmitter_name")]
    public string? TransmitterName { get; init; }

    [JsonPropertyName("receiver_id")]
    public string? ReceiverId { get; init; }

    [JsonPropertyName("receiver_name")]
    public string? ReceiverName { get; init; }

    [JsonPropertyName("invoice_number")]
    public string? InvoiceNumber { get; init; }

    [JsonPropertyName("invoice_date")]
    public DateOnly? InvoiceDate { get; init; }

    [JsonPropertyName("invoice_cufe")]
    public string? InvoiceCufe { get; init; }

    [JsonPropertyName("invoice_total")]
    public decimal? InvoiceTotal { get; init; }

    [JsonPropertyName("invoice_tax")]
    public decimal? InvoiceTax { get; init; }
}

internal static class RadianMapping
{
    public static string StatusName(DocumentEventStatus status) => status switch
    {
        DocumentEventStatus.Accepted => "accepted",
        DocumentEventStatus.Rejected => "rejected",
        _ => "unconfirmed",
    };

    /// <summary>Oldest first; events of the same instant in the order of the flow.</summary>
    public static IEnumerable<DocumentEvent> Ordered(IEnumerable<DocumentEvent> events) =>
        events.OrderBy(e => e.IssuedAt).ThenBy(e => RadianCatalog.SequenceOf(e.EventId));

    public static DocumentEventResponse Event(DocumentEvent row)
    {
        var code = RadianCatalog.ById(row.EventId)?.Code ?? row.EventId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new DocumentEventResponse
        {
            Id = row.Id,
            EventCode = code,
            EventName = RadianCatalog.NameOf(code),
            Status = StatusName(row.Status),
            Cude = row.Cude,
            Number = row.Number,
            IssuedAt = row.IssuedAt,
            ClaimReason = row.RejectionCode,
            DianStatusCode = row.DianStatusCode,
            DianStatusDescription = row.DianStatusDescription,
            ErrorMessages = row.ErrorMessages ?? [],
        };
    }

    public static ReceivedDocumentResponse Received(ReceivedDocument row, IEnumerable<DocumentEvent> events) => new()
    {
        Id = row.Id,
        SupplierIdentificationNumber = row.SupplierIdentificationNumber,
        SupplierDv = row.SupplierDv,
        SupplierName = row.SupplierName,
        SupplierEmail = row.SupplierEmail,
        TypeDocumentId = row.DocumentTypeId,
        Prefix = row.Prefix,
        Number = row.Number,
        Cufe = row.Cufe,
        IssueDate = row.IssueDate,
        TypeEnvironmentId = row.TypeEnvironmentId,
        LineExtensionAmount = row.LineExtensionAmount,
        TaxExclusiveAmount = row.TaxExclusiveAmount,
        AllowanceTotalAmount = row.AllowanceTotalAmount,
        TaxAmount = row.TaxAmount,
        PayableAmount = row.PayableAmount,
        CreatedAt = row.CreatedAt,
        Events = Ordered(events).Select(Event).ToList(),
    };
}
