using System.Globalization;
using System.Text.Json;
using ImpiFact.Dian.Cufe;
using ImpiFact.Dian.Ubl;
using static ImpiFact.Golden.Tests.ApiDianJson;

namespace ImpiFact.Golden.Tests;

/// <summary>
/// Convierte una solicitud de nota crédito o débito de ApiDian (el input.json de un caso de referencia,
/// cuyo XML y CUDE generó el PHP original) más la configuración fija de captura al modelo
/// <see cref="SalesNote"/> de ImpiFact.Dian. Las partes, el pago, los impuestos, los totales y las líneas se
/// convierten con <see cref="ApiDianInvoiceAdapter"/>, igual que en la factura; aquí solo se agrega lo
/// propio de las notas (CreditNoteController.php y DebitNoteController.php de ApiDian).
/// </summary>
public sealed class ApiDianNoteAdapter(ApiDianCatalog catalog)
{
    /// <summary>type_document_id de ApiDian: 4 nota crédito, 5 nota débito.</summary>
    private const int CreditNoteDocumentId = 4;
    private const int DebitNoteDocumentId = 5;

    /// <summary>
    /// type_operation_id que ApiDian usa cuando la solicitud no lo trae: 12 (código 20, nota crédito que
    /// referencia una factura) y 6 (código 30, nota débito que referencia una factura).
    /// </summary>
    private const int DefaultCreditOperationId = 12;
    private const int DefaultDebitOperationId = 6;

    private readonly ApiDianInvoiceAdapter _shared = new(catalog);

    public SalesNote Map(JsonElement request)
    {
        var documentId = RequiredInt(request, "type_document_id");
        var kind = documentId switch
        {
            CreditNoteDocumentId => NoteKind.Credit,
            DebitNoteDocumentId => NoteKind.Debit,
            _ => throw new InvalidDataException($"type_document_id {documentId} is not a credit (4) or debit (5) note"),
        };

        // Solo se valida contra el catálogo: el código del tipo (91) lo escribe el constructor.
        catalog.Get("documentTypes", documentId);

        var isCredit = kind == NoteKind.Credit;
        var operationId = OptionalText(request, "type_operation_id") ?? (isCredit ? DefaultCreditOperationId : DefaultDebitOperationId)
            .ToString(CultureInfo.InvariantCulture);
        return new SalesNote
        {
            Kind = kind,
            Environment = (DianEnvironment)GoldenCaptureContext.EnvironmentId,
            QrCodeBaseUrl = GoldenCaptureContext.QrCodeBaseUrl,
            OperationTypeCode = catalog.Get("operationTypes", operationId).Code,
            Prefix = RequiredText(request, "prefix"),
            Number = long.Parse(RequiredText(request, "number"), CultureInfo.InvariantCulture),
            IssueDate = RequiredDate(request, "date"),
            IssueTime = TimeOnly.ParseExact(RequiredText(request, "time"), "HH:mm:ss", CultureInfo.InvariantCulture),
            Note = OptionalText(request, "notes"),
            Software = new SoftwareCredentials(GoldenCaptureContext.SoftwareId, GoldenCaptureContext.SoftwarePin),
            LocalCurrencyCode = catalog.Get("currencies", GoldenCaptureContext.CompanyCurrencyId).Code,
            ForeignCurrency = _shared.MapForeignCurrency(request),
            Supplier = _shared.MapSupplier(),
            Customer = _shared.MapCustomer(request.GetProperty("customer")),
            Payments = _shared.MapPayments(request.GetProperty("payment_form")),
            InvoicePeriod = MapInvoicePeriod(request),
            Discrepancy = MapDiscrepancy(request, kind),
            BillingReference = MapBillingReference(request),
            AllowanceCharges = MapList(request, "allowance_charges", _shared.MapAllowanceCharge),
            TaxSubtotals = MapList(request, "tax_totals", _shared.MapTaxSubtotal),
            WithholdingSubtotals = MapList(request, "with_holding_tax_total", _shared.MapTaxSubtotal),

            // La nota crédito trae legal_monetary_totals; la débito, requested_monetary_totals.
            Totals = ApiDianInvoiceAdapter.MapTotals(request.GetProperty(isCredit ? "legal_monetary_totals" : "requested_monetary_totals")),
            Lines = MapList(request, isCredit ? "credit_note_lines" : "debit_note_lines", _shared.MapLine),
        };
    }

    private static InvoicePeriod? MapInvoicePeriod(JsonElement request) =>
        request.TryGetProperty("invoice_period", out var period) && period.ValueKind == JsonValueKind.Object
            ? new InvoicePeriod(RequiredDate(period, "start_date"), RequiredDate(period, "end_date"))
            : null;

    /// <summary>
    /// La descripción sale del catálogo de discrepancias de cada tipo de nota, no del campo
    /// discrepancyresponsedescription de la solicitud, que ApiDian ignora.
    /// </summary>
    private DiscrepancyResponse? MapDiscrepancy(JsonElement request, NoteKind kind)
    {
        var code = OptionalText(request, "discrepancyresponsecode");
        if (code is null)
        {
            return null;
        }

        var entry = catalog.Get(kind == NoteKind.Credit ? "creditNoteDiscrepancies" : "debitNoteDiscrepancies", code);
        return new DiscrepancyResponse(code, entry.Name);
    }

    private static BillingReference? MapBillingReference(JsonElement request) =>
        request.TryGetProperty("billing_reference", out var reference) && reference.ValueKind == JsonValueKind.Object
            ? new BillingReference(RequiredText(reference, "number"), RequiredText(reference, "uuid"), RequiredDate(reference, "issue_date"))
            : null;
}
