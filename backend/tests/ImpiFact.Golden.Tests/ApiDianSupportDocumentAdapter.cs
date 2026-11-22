using System.Globalization;
using System.Text.Json;
using ImpiFact.Dian.Cufe;
using ImpiFact.Dian.Ubl;
using static ImpiFact.Golden.Tests.ApiDianJson;

namespace ImpiFact.Golden.Tests;

/// <summary>
/// Converts an ApiDian support document (type 11, SupportDocumentController) or adjustment note (type 13,
/// sdCreditNoteController) request plus the fixed capture context into the ImpiFact.Dian model, like
/// <see cref="ApiDianInvoiceAdapter"/> and <see cref="ApiDianNoteAdapter"/> do for the invoice and the notes. The seller
/// goes in the supplier position of the XML and the company in the customer one, as templates 05 and 95 lay them out.
/// </summary>
public sealed class ApiDianSupportDocumentAdapter(ApiDianCatalog catalog)
{
    /// <summary>type_operation_id of SupportDocumentController when the request has none (23, resident, code 10).</summary>
    private const int DefaultSupportOperationId = 23;

    private readonly ApiDianInvoiceAdapter _shared = new(catalog);

    public SalesInvoice MapSupportDocument(JsonElement request)
    {
        var operationId = OptionalText(request, "type_operation_id") ?? DefaultSupportOperationId.ToString(CultureInfo.InvariantCulture);
        var lines = request.GetProperty("invoice_lines").EnumerateArray().ToList();
        return new SalesInvoice
        {
            Environment = (DianEnvironment)GoldenCaptureContext.EnvironmentId,
            QrCodeBaseUrl = GoldenCaptureContext.QrCodeBaseUrl,
            OperationTypeCode = catalog.Get("operationTypes", operationId).Code,
            InvoiceTypeCode = catalog.Get("documentTypes", RequiredInt(request, "type_document_id")).Code,
            Resolution = new NumberingResolution(
                GoldenCaptureContext.ResolutionNumber,
                GoldenCaptureContext.SupportDocumentDateFrom,
                GoldenCaptureContext.ResolutionDateTo,
                RequiredText(request, "prefix"),
                GoldenCaptureContext.SupportDocumentFrom,
                GoldenCaptureContext.SupportDocumentTo,
                TechnicalKey: ""),
            Number = long.Parse(RequiredText(request, "number"), CultureInfo.InvariantCulture),
            IssueDate = RequiredDate(request, "date"),
            IssueTime = TimeOnly.ParseExact(RequiredText(request, "time"), "HH:mm:ss", CultureInfo.InvariantCulture),
            Software = new SoftwareCredentials(GoldenCaptureContext.SupportDocumentSoftwareId, GoldenCaptureContext.SupportDocumentSoftwarePin),
            LocalCurrencyCode = catalog.Get("currencies", GoldenCaptureContext.CompanyCurrencyId).Code,
            ForeignCurrency = _shared.MapForeignCurrency(request),
            SupportDocument = new SupportDocumentTerms(GoldenCaptureContext.SupportDocumentCaptureDate),
            Supplier = MapSeller(request.GetProperty("seller")),
            Customer = _shared.MapSupplier(),
            Payments = _shared.MapPayments(request.GetProperty("payment_form")),
            AllowanceCharges = MapList(request, "allowance_charges", _shared.MapAllowanceCharge),
            TaxSubtotals = MapList(request, "tax_totals", _shared.MapTaxSubtotal),
            WithholdingSubtotals = MapList(request, "with_holding_tax_total", _shared.MapTaxSubtotal),
            Note = OptionalText(request, "notes"),
            Totals = ApiDianInvoiceAdapter.MapTotals(request.GetProperty("legal_monetary_totals")),
            Lines = MapList(request, "invoice_lines", _shared.MapLine).Select((line, index) => line with
            {
                GenerationPeriod = new LineGenerationPeriod(
                    RequiredDate(lines[index], "start_date"),
                    RequiredText(lines[index], "type_generation_transmition_id"),
                    catalog.Get("generationTransmitions", RequiredInt(lines[index], "type_generation_transmition_id")).Name),
            }).ToList(),
        };
    }

    public SalesNote MapAdjustmentNote(JsonElement request)
    {
        var operationId = OptionalText(request, "type_operation_id")
            ?? GoldenCaptureContext.AdjustmentNoteOperationTypeId.ToString(CultureInfo.InvariantCulture);
        var code = OptionalText(request, "discrepancyresponsecode");
        var reference = request.GetProperty("billing_reference");
        var lines = request.GetProperty("credit_note_lines").EnumerateArray().ToList();
        return new SalesNote
        {
            Kind = NoteKind.SupportDocumentAdjustment,
            Environment = (DianEnvironment)GoldenCaptureContext.EnvironmentId,
            QrCodeBaseUrl = GoldenCaptureContext.QrCodeBaseUrl,
            OperationTypeCode = catalog.Get("operationTypes", operationId).Code,
            Prefix = RequiredText(request, "prefix"),
            Number = long.Parse(RequiredText(request, "number"), CultureInfo.InvariantCulture),
            IssueDate = RequiredDate(request, "date"),
            IssueTime = TimeOnly.ParseExact(RequiredText(request, "time"), "HH:mm:ss", CultureInfo.InvariantCulture),
            Note = OptionalText(request, "notes"),
            Software = new SoftwareCredentials(GoldenCaptureContext.SupportDocumentSoftwareId, GoldenCaptureContext.SupportDocumentSoftwarePin),
            LocalCurrencyCode = catalog.Get("currencies", GoldenCaptureContext.CompanyCurrencyId).Code,
            ForeignCurrency = _shared.MapForeignCurrency(request),
            Supplier = MapSeller(request.GetProperty("seller")),
            Customer = _shared.MapSupplier(),

            // sdCreditNoteController uses cash and payment method 10 when the request has no payment_form.
            Payments = request.TryGetProperty("payment_form", out var payment)
                ? _shared.MapPayments(payment)
                : [new PaymentTerms(catalog.Get("paymentForms", 1).Code, catalog.Get("paymentMethods", 10).Code, null, null)],
            Discrepancy = code is null ? null : new DiscrepancyResponse(code, catalog.Get("supportDocumentDiscrepancies", code).Name),
            BillingReference = new BillingReference(RequiredText(reference, "number"), RequiredText(reference, "uuid"), RequiredDate(reference, "issue_date"))
            {
                SchemeName = "CUDS-SHA384",
            },
            AllowanceCharges = MapList(request, "allowance_charges", _shared.MapAllowanceCharge),
            TaxSubtotals = MapList(request, "tax_totals", _shared.MapTaxSubtotal),
            WithholdingSubtotals = MapList(request, "with_holding_tax_total", _shared.MapTaxSubtotal),
            Totals = ApiDianInvoiceAdapter.MapTotals(request.GetProperty("legal_monetary_totals")),
            Lines = MapList(request, "credit_note_lines", _shared.MapLine).Select((line, index) => line with
            {
                BrandName = OptionalText(lines[index], "brandname"),
                ModelName = OptionalText(lines[index], "modelname"),
            }).ToList(),
        };
    }

    /// <summary>
    /// The seller not obliged to invoice, as <c>_accounting.blade.php</c> renders it without <c>$supplier</c>: the
    /// customer rules of the invoice (including the foreign address) plus <c>postal_zone_code</c>, and no cac:Contact,
    /// because that block reads <c>customer.phone</c> and <c>customer.email</c>, which a support document does not send.
    /// </summary>
    private InvoiceParty MapSeller(JsonElement seller) => _shared.MapCustomer(seller) with
    {
        Telephone = null,
        Email = null,
        PostalZone = RequiredText(seller, "postal_zone_code"),
    };
}
