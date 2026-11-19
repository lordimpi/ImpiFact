using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ImpiFact.Application.Features.Documents.Issue;

// Cuerpo JSON de la emisión heredada (anexo A.1 a A.3; php-rules, secciones 3 y 4), con los nombres del contrato,
// incluidos sus errores de escritura (discrepancyresponsecode). Un mismo cuerpo sirve para la factura y las notas:
// cada tipo usa su lista de líneas y sus totales. Los valores simples llegan como LegacyScalar porque el contrato
// mezcla texto y números; los campos que ImpiFact todavía no usa (plantillas, PDF…) se aceptan y se ignoran. Los del
// correo (sendmail, sendmailtome, email_pos_customer) se declaran para el contrato y los lee DocumentEmailRequest.

/// <summary>Solicitud de emisión de factura (<c>invoice</c>), nota crédito (<c>credit-note</c>) o nota débito (<c>debit-note</c>).</summary>
public sealed record IssueDocumentBody
{
    [JsonPropertyName("number")]
    public LegacyScalar? Number { get; init; }

    [JsonPropertyName("type_document_id")]
    public LegacyScalar? TypeDocumentId { get; init; }

    [JsonPropertyName("date")]
    public LegacyScalar? Date { get; init; }

    [JsonPropertyName("time")]
    public LegacyScalar? Time { get; init; }

    [JsonPropertyName("resolution_number")]
    public LegacyScalar? ResolutionNumber { get; init; }

    [JsonPropertyName("prefix")]
    public LegacyScalar? Prefix { get; init; }

    [JsonPropertyName("notes")]
    public LegacyScalar? Notes { get; init; }

    [JsonPropertyName("head_note")]
    public LegacyScalar? HeadNote { get; init; }

    [JsonPropertyName("foot_note")]
    public LegacyScalar? FootNote { get; init; }

    [JsonPropertyName("type_operation_id")]
    public LegacyScalar? TypeOperationId { get; init; }

    /// <summary>Moneda de negociación cuando no es la de la empresa.</summary>
    [JsonPropertyName("idcurrency")]
    public LegacyScalar? IdCurrency { get; init; }

    [JsonPropertyName("calculationrate")]
    public LegacyScalar? CalculationRate { get; init; }

    [JsonPropertyName("calculationratedate")]
    public LegacyScalar? CalculationRateDate { get; init; }

    [JsonPropertyName("customer")]
    public CustomerBody? Customer { get; init; }

    /// <summary>El contrato admite un objeto o una lista de objetos; se lee siempre como lista.</summary>
    [JsonPropertyName("payment_form")]
    [JsonConverter(typeof(ObjectOrListConverter<PaymentFormBody>))]
    public List<PaymentFormBody>? PaymentForm { get; init; }

    /// <summary>
    /// Un anticipo suelto (<c>prepaid_payment</c>), como en el PHP. Solo lo usa la factura: las plantillas de las notas
    /// no escriben anticipos.
    /// </summary>
    [JsonPropertyName("prepaid_payment")]
    public PrepaidPaymentBody? PrepaidPayment { get; init; }

    /// <summary>Varios anticipos (<c>prepaid_payments</c>); van después de <c>prepaid_payment</c>. Solo en la factura.</summary>
    [JsonPropertyName("prepaid_payments")]
    public List<PrepaidPaymentBody>? PrepaidPayments { get; init; }

    [JsonPropertyName("allowance_charges")]
    public List<AllowanceChargeBody>? AllowanceCharges { get; init; }

    [JsonPropertyName("tax_totals")]
    public List<TaxTotalBody>? TaxTotals { get; init; }

    [JsonPropertyName("with_holding_tax_total")]
    public List<TaxTotalBody>? WithHoldingTaxTotal { get; init; }

    /// <summary>Totales de la factura y de la nota crédito.</summary>
    [JsonPropertyName("legal_monetary_totals")]
    public MonetaryTotalsBody? LegalMonetaryTotals { get; init; }

    /// <summary>Totales de la nota débito.</summary>
    [JsonPropertyName("requested_monetary_totals")]
    public MonetaryTotalsBody? RequestedMonetaryTotals { get; init; }

    [JsonPropertyName("invoice_lines")]
    public List<LineBody>? InvoiceLines { get; init; }

    [JsonPropertyName("credit_note_lines")]
    public List<LineBody>? CreditNoteLines { get; init; }

    [JsonPropertyName("debit_note_lines")]
    public List<LineBody>? DebitNoteLines { get; init; }

    [JsonPropertyName("discrepancyresponsecode")]
    public LegacyScalar? DiscrepancyResponseCode { get; init; }

    /// <summary>El PHP lo ignora: la descripción del XML sale del catálogo. Se guarda en el documento.</summary>
    [JsonPropertyName("discrepancyresponsedescription")]
    public LegacyScalar? DiscrepancyResponseDescription { get; init; }

    [JsonPropertyName("billing_reference")]
    public BillingReferenceBody? BillingReference { get; init; }

    [JsonPropertyName("invoice_period")]
    public InvoicePeriodBody? InvoicePeriod { get; init; }

    /// <summary>
    /// Enviar el documento por correo al adquiriente cuando la DIAN lo acepte (verdadero como en el PHP: <c>true</c>,
    /// <c>1</c> o un texto distinto de <c>"0"</c>). Sin este campo no se envía correo. El envío lo lee
    /// <c>DocumentEmailRequest</c> del JSON guardado; aquí se declara para que el contrato lo muestre.
    /// </summary>
    [JsonPropertyName("sendmail")]
    public LegacyScalar? SendMail { get; init; }

    /// <summary>Con <c>sendmail</c>, enviar también una copia al correo de la empresa emisora.</summary>
    [JsonPropertyName("sendmailtome")]
    public LegacyScalar? SendMailToMe { get; init; }

    /// <summary>
    /// Correo del comprador cuando es el consumidor final (<c>222222222222</c>): sin él, al consumidor final no se le
    /// envía correo aunque la solicitud traiga <c>sendmail</c>.
    /// </summary>
    [JsonPropertyName("email_pos_customer")]
    public LegacyScalar? EmailPosCustomer { get; init; }
}

/// <summary>Adquiriente (<c>customer</c>).</summary>
public sealed record CustomerBody
{
    [JsonPropertyName("identification_number")]
    public LegacyScalar? IdentificationNumber { get; init; }

    [JsonPropertyName("dv")]
    public LegacyScalar? Dv { get; init; }

    [JsonPropertyName("type_document_identification_id")]
    public LegacyScalar? TypeDocumentIdentificationId { get; init; }

    [JsonPropertyName("type_organization_id")]
    public LegacyScalar? TypeOrganizationId { get; init; }

    [JsonPropertyName("type_regime_id")]
    public LegacyScalar? TypeRegimeId { get; init; }

    [JsonPropertyName("type_liability_id")]
    public LegacyScalar? TypeLiabilityId { get; init; }

    [JsonPropertyName("tax_id")]
    public LegacyScalar? TaxId { get; init; }

    [JsonPropertyName("language_id")]
    public LegacyScalar? LanguageId { get; init; }

    [JsonPropertyName("country_id")]
    public LegacyScalar? CountryId { get; init; }

    [JsonPropertyName("municipality_id")]
    public LegacyScalar? MunicipalityId { get; init; }

    [JsonPropertyName("name")]
    public LegacyScalar? Name { get; init; }

    [JsonPropertyName("phone")]
    public LegacyScalar? Phone { get; init; }

    [JsonPropertyName("address")]
    public LegacyScalar? Address { get; init; }

    [JsonPropertyName("email")]
    public LegacyScalar? Email { get; init; }

    [JsonPropertyName("merchant_registration")]
    public LegacyScalar? MerchantRegistration { get; init; }
}

/// <summary>Forma y medio de pago.</summary>
public sealed record PaymentFormBody
{
    [JsonPropertyName("payment_form_id")]
    public LegacyScalar? PaymentFormId { get; init; }

    [JsonPropertyName("payment_method_id")]
    public LegacyScalar? PaymentMethodId { get; init; }

    [JsonPropertyName("payment_due_date")]
    public LegacyScalar? PaymentDueDate { get; init; }

    [JsonPropertyName("duration_measure")]
    public LegacyScalar? DurationMeasure { get; init; }
}

/// <summary>Un anticipo ya recibido (cac:PrepaidPayment del XML), con los nombres del contrato del PHP.</summary>
public sealed record PrepaidPaymentBody
{
    /// <summary>Identificación del pago (cbc:ID).</summary>
    [JsonPropertyName("idpayment")]
    public LegacyScalar? IdPayment { get; init; }

    /// <summary>Valor pagado por anticipado (cbc:PaidAmount).</summary>
    [JsonPropertyName("paidamount")]
    public LegacyScalar? PaidAmount { get; init; }

    /// <summary>Fecha <c>yyyy-MM-dd</c> en que se recibió el pago (cbc:ReceivedDate).</summary>
    [JsonPropertyName("receiveddate")]
    public LegacyScalar? ReceivedDate { get; init; }

    /// <summary>Fecha <c>yyyy-MM-dd</c> en que se hizo el pago (cbc:PaidDate); opcional.</summary>
    [JsonPropertyName("paiddate")]
    public LegacyScalar? PaidDate { get; init; }

    /// <summary>Instrucciones relativas al pago (cbc:InstructionID); opcional.</summary>
    [JsonPropertyName("instructionid")]
    public LegacyScalar? InstructionId { get; init; }

    /// <summary>
    /// Verdadero si no trae ningún campo. El PHP descarta un <c>prepaid_payment</c> vacío (<c>if($request->prepaid_payment)</c>).
    /// </summary>
    [JsonIgnore]
    public bool IsEmpty => IdPayment is null && PaidAmount is null && ReceivedDate is null && PaidDate is null && InstructionId is null;
}

/// <summary>Descuento o cargo, de la cabecera o de una línea.</summary>
public sealed record AllowanceChargeBody
{
    [JsonPropertyName("charge_indicator")]
    public LegacyScalar? ChargeIndicator { get; init; }

    [JsonPropertyName("discount_id")]
    public LegacyScalar? DiscountId { get; init; }

    [JsonPropertyName("allowance_charge_reason")]
    public LegacyScalar? AllowanceChargeReason { get; init; }

    [JsonPropertyName("multiplier_factor_numeric")]
    public LegacyScalar? MultiplierFactorNumeric { get; init; }

    [JsonPropertyName("amount")]
    public LegacyScalar? Amount { get; init; }

    [JsonPropertyName("base_amount")]
    public LegacyScalar? BaseAmount { get; init; }
}

/// <summary>Subtotal de un impuesto o de una retención.</summary>
public sealed record TaxTotalBody
{
    [JsonPropertyName("tax_id")]
    public LegacyScalar? TaxId { get; init; }

    [JsonPropertyName("tax_amount")]
    public LegacyScalar? TaxAmount { get; init; }

    [JsonPropertyName("percent")]
    public LegacyScalar? Percent { get; init; }

    [JsonPropertyName("taxable_amount")]
    public LegacyScalar? TaxableAmount { get; init; }

    [JsonPropertyName("unit_measure_id")]
    public LegacyScalar? UnitMeasureId { get; init; }

    [JsonPropertyName("per_unit_amount")]
    public LegacyScalar? PerUnitAmount { get; init; }

    [JsonPropertyName("base_unit_measure")]
    public LegacyScalar? BaseUnitMeasure { get; init; }
}

/// <summary>Totales del documento.</summary>
public sealed record MonetaryTotalsBody
{
    [JsonPropertyName("line_extension_amount")]
    public LegacyScalar? LineExtensionAmount { get; init; }

    [JsonPropertyName("tax_exclusive_amount")]
    public LegacyScalar? TaxExclusiveAmount { get; init; }

    [JsonPropertyName("tax_inclusive_amount")]
    public LegacyScalar? TaxInclusiveAmount { get; init; }

    [JsonPropertyName("allowance_total_amount")]
    public LegacyScalar? AllowanceTotalAmount { get; init; }

    [JsonPropertyName("charge_total_amount")]
    public LegacyScalar? ChargeTotalAmount { get; init; }

    [JsonPropertyName("pre_paid_amount")]
    public LegacyScalar? PrePaidAmount { get; init; }

    [JsonPropertyName("payable_amount")]
    public LegacyScalar? PayableAmount { get; init; }
}

/// <summary>Línea de la factura o de la nota.</summary>
public sealed record LineBody
{
    [JsonPropertyName("unit_measure_id")]
    public LegacyScalar? UnitMeasureId { get; init; }

    [JsonPropertyName("invoiced_quantity")]
    public LegacyScalar? InvoicedQuantity { get; init; }

    [JsonPropertyName("line_extension_amount")]
    public LegacyScalar? LineExtensionAmount { get; init; }

    [JsonPropertyName("free_of_charge_indicator")]
    public LegacyScalar? FreeOfChargeIndicator { get; init; }

    [JsonPropertyName("reference_price_id")]
    public LegacyScalar? ReferencePriceId { get; init; }

    [JsonPropertyName("description")]
    public LegacyScalar? Description { get; init; }

    [JsonPropertyName("notes")]
    public LegacyScalar? Notes { get; init; }

    [JsonPropertyName("code")]
    public LegacyScalar? Code { get; init; }

    [JsonPropertyName("type_item_identification_id")]
    public LegacyScalar? TypeItemIdentificationId { get; init; }

    [JsonPropertyName("price_amount")]
    public LegacyScalar? PriceAmount { get; init; }

    [JsonPropertyName("base_quantity")]
    public LegacyScalar? BaseQuantity { get; init; }

    [JsonPropertyName("allowance_charges")]
    public List<AllowanceChargeBody>? AllowanceCharges { get; init; }

    [JsonPropertyName("tax_totals")]
    public List<TaxTotalBody>? TaxTotals { get; init; }
}

/// <summary>Factura a la que se refiere una nota.</summary>
public sealed record BillingReferenceBody
{
    [JsonPropertyName("number")]
    public LegacyScalar? Number { get; init; }

    [JsonPropertyName("uuid")]
    public LegacyScalar? Uuid { get; init; }

    [JsonPropertyName("issue_date")]
    public LegacyScalar? IssueDate { get; init; }
}

/// <summary>Periodo facturado de una nota sin referencia.</summary>
public sealed record InvoicePeriodBody
{
    [JsonPropertyName("start_date")]
    public LegacyScalar? StartDate { get; init; }

    [JsonPropertyName("end_date")]
    public LegacyScalar? EndDate { get; init; }
}

/// <summary>Lee un objeto suelto como una lista de un elemento, o una lista de objetos tal cual.</summary>
public sealed class ObjectOrListConverter<T> : JsonConverter<List<T>>
{
    public override List<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var single = JsonSerializer.Deserialize<T>(ref reader, options);
            return single is null ? [] : [single];
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            return JsonSerializer.Deserialize<List<T>>(ref reader, options) ?? [];
        }

        throw new JsonException($"Expected an object or a list but found {reader.TokenType}.");
    }

    public override void Write(Utf8JsonWriter writer, List<T> value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}

/// <summary>
/// Lee el cuerpo de la emisión y prepara la copia que se guarda (<c>document_files</c> de tipo solicitud). Si el
/// cuerpo trae los datos del servidor de correo (<c>smtp_parameters</c>, con su contraseña), esa parte no se guarda:
/// el PHP la guardaba en claro (php-rules, sección 3.7).
/// </summary>
public static class IssueDocumentJson
{
    /// <summary>Campos que nunca se guardan con la solicitud porque traen secretos.</summary>
    private static readonly string[] SecretFields = ["smtp_parameters"];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = false,
        MaxDepth = 32,
    };

    /// <summary>Lee el cuerpo; falso si no es un objeto JSON con la forma del contrato.</summary>
    public static bool TryParse(byte[] body, out IssueDocumentBody? document)
    {
        document = null;
        try
        {
            // Una propiedad repetida es ambigua (¿cuál valor vale?): se rechaza como JSON inválido.
            using var parsed = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = Options.MaxDepth, AllowDuplicateProperties = false });
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            document = parsed.RootElement.Deserialize<IssueDocumentBody>(Options);
            return document is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Bytes que se guardan como solicitud: el cuerpo tal cual, o sin sus campos secretos si los trae.</summary>
    public static byte[] ForStorage(byte[] body)
    {
        // Se buscan sin distinguir mayúsculas: «SMTP_PARAMETERS» también es un secreto aunque el contrato no lo lea.
        var root = JsonNode.Parse(body) as JsonObject;
        var secrets = root?.Select(property => property.Key)
            .Where(key => SecretFields.Contains(key, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (root is null || secrets is not { Count: > 0 })
        {
            return body;
        }

        foreach (var field in secrets)
        {
            root.Remove(field);
        }

        return JsonSerializer.SerializeToUtf8Bytes(root);
    }
}
