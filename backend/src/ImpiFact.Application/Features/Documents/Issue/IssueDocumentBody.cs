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

    /// <summary>
    /// AIU invoice only (required there): object of the contract. It goes as «Contrato de servicios AIU por concepto de:
    /// {noteAIU}» in the note of every line whose <c>description</c> is exactly «Administración». The other documents
    /// ignore it.
    /// </summary>
    [JsonPropertyName("noteAIU")]
    public LegacyScalar? NoteAiu { get; init; }

    /// <summary>
    /// Contingency invoice type 03 only (required there): number of the paper invoice it transcribes
    /// (cac:AdditionalDocumentReference/cbc:ID). The other documents ignore it.
    /// </summary>
    [JsonPropertyName("AdditionalDocumentReferenceID")]
    public LegacyScalar? AdditionalDocumentReferenceId { get; init; }

    /// <summary>Contingency invoice type 03 only (required there): date of the paper invoice (<c>yyyy-MM-dd</c>).</summary>
    [JsonPropertyName("AdditionalDocumentReferenceDate")]
    public LegacyScalar? AdditionalDocumentReferenceDate { get; init; }

    /// <summary>
    /// Contingency invoice type 03 only (required there): DIAN document type code of the paper invoice, two digits (for
    /// example <c>01</c>, sales invoice).
    /// </summary>
    [JsonPropertyName("AdditionalDocumentReferenceTypeDocument")]
    public LegacyScalar? AdditionalDocumentReferenceTypeDocument { get; init; }

    /// <summary>
    /// Export invoice only (required there): delivery terms with the Incoterm. The other documents ignore it.
    /// </summary>
    [JsonPropertyName("deliveryterms")]
    public DeliveryTermsBody? DeliveryTerms { get; init; }

    /// <summary>
    /// Export invoice only, optional: supplement K of the DIAN annex 1.9 (export data and totals in pesos for the graphic
    /// representation). When sent it needs all of its keys: <c>responsible_incharge</c>, <c>departure_place</c>,
    /// <c>conveyance</c>, <c>transport_document_type</c>, <c>transport_document_number</c>, <c>transporter_processor</c>,
    /// <c>merchandise_origin_country</c>, <c>destination</c>, <c>payment_means</c>, <c>insurance_carrier</c>,
    /// <c>observations</c> and the totals <c>FctConvCop</c> (exchange rate), <c>MonedaCop</c>, <c>SubTotalCop</c>,
    /// <c>DescuentoDetalleCop</c>, <c>RecargoDetalleCop</c>, <c>TotalBrutoFacturaCop</c>, <c>TotIvaCop</c>,
    /// <c>TotIncCop</c>, <c>TotBolCop</c>, <c>ImpOtroCop</c>, <c>MntImpCop</c>, <c>TotalNetoFacturaCop</c>,
    /// <c>MntDctoCop</c>, <c>MntRcgoCop</c>, <c>VlrPagarCop</c>, <c>ReteFueCop</c>, <c>ReteIvaCop</c>,
    /// <c>ReteIcaCop</c> and <c>TotAnticiposCop</c>. Other keys are ignored.
    /// </summary>
    [JsonPropertyName("k_supplement")]
    public Dictionary<string, LegacyScalar?>? KSupplement { get; init; }

    /// <summary>
    /// National invoice only (<c>POST /api/v1/documents/invoices</c>, its test-set route and <c>/api/ubl2.1/invoice</c>),
    /// optional: makes it a health-sector invoice without RIPS (Ministerio de Salud, Resolución 948 de 2026, Documento
    /// Técnico 2 v001). The other documents ignore it, as ApiDian Community did.
    /// </summary>
    [JsonPropertyName("health_fields")]
    public HealthFieldsBody? HealthFields { get; init; }

    /// <summary>Moneda de negociación cuando no es la de la empresa.</summary>
    [JsonPropertyName("idcurrency")]
    public LegacyScalar? IdCurrency { get; init; }

    [JsonPropertyName("calculationrate")]
    public LegacyScalar? CalculationRate { get; init; }

    [JsonPropertyName("calculationratedate")]
    public LegacyScalar? CalculationRateDate { get; init; }

    [JsonPropertyName("customer")]
    public CustomerBody? Customer { get; init; }

    /// <summary>
    /// Support document and its adjustment note only (required there, instead of <c>customer</c>): the seller not obliged
    /// to invoice from whom the company buys. Same fields as <c>customer</c>, with <c>phone</c>, <c>address</c>,
    /// <c>merchant_registration</c> and <c>postal_zone_code</c> required.
    /// </summary>
    [JsonPropertyName("seller")]
    public CustomerBody? Seller { get; init; }

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

    /// <summary>
    /// Export invoice only: city of a customer outside Colombia (<c>country_id</c> other than 46), written as
    /// cbc:CityName of its address; the other documents ignore it.
    /// </summary>
    [JsonPropertyName("municipality_name")]
    public LegacyScalar? MunicipalityName { get; init; }

    /// <summary>Export invoice only: state or province of a customer outside Colombia (cbc:CountrySubentity).</summary>
    [JsonPropertyName("state_name")]
    public LegacyScalar? StateName { get; init; }

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

    /// <summary>Support-document seller only (required there): postal code, numeric (cbc:PostalZone).</summary>
    [JsonPropertyName("postal_zone_code")]
    public LegacyScalar? PostalZoneCode { get; init; }
}

/// <summary>
/// Health-sector data of an invoice (<c>health_fields</c>), with the names of the ApiDian Postman collection (folder 07,
/// «Sector salud»). The XML follows the Documento Técnico 2 v001: the billing period, the health operation and one Collection
/// built from <c>users_info</c>, whose users must share provider, payment method, coverage, contract, policy and
/// justification. The copay, moderating fee, shared payment and advance go to the XML through <c>prepaid_payment(s)</c> with
/// <c>prepaid_payment_type_id</c>; the patient data belongs to the RIPS (section 6.b) and is only validated.
/// </summary>
public sealed record HealthFieldsBody
{
    /// <summary>Start of the billing period (cac:InvoicePeriod/cbc:StartDate), <c>yyyy-MM-dd</c>; required.</summary>
    [JsonPropertyName("invoice_period_start_date")]
    public LegacyScalar? InvoicePeriodStartDate { get; init; }

    /// <summary>End of the billing period (cbc:EndDate), <c>yyyy-MM-dd</c>, on or after the start; required.</summary>
    [JsonPropertyName("invoice_period_end_date")]
    public LegacyScalar? InvoicePeriodEndDate { get; init; }

    /// <summary>
    /// Health operation (cbc:CustomizationID), required: 1 SS-CUFE, 2 SS-CUDE, 3 SS-POS, 4 SS-SNum, 5 SS-Recaudo (no
    /// extension: <c>users_info</c> empty), 6 SS-Reporte or 7 SS-SinAporte (extension optional).
    /// </summary>
    [JsonPropertyName("health_type_operation_id")]
    public LegacyScalar? HealthTypeOperationId { get; init; }

    /// <summary>Accepted for the contract; ImpiFact's PDF does not print the users yet.</summary>
    [JsonPropertyName("print_users_info_to_pdf")]
    public LegacyScalar? PrintUsersInfoToPdf { get; init; }

    /// <summary>The users (patients) of the invoice; at least one unless the operation is 5 or 7.</summary>
    [JsonPropertyName("users_info")]
    public List<HealthUserBody>? UsersInfo { get; init; }
}

/// <summary>A user (patient) of a health-sector invoice (<c>health_fields.users_info[]</c>).</summary>
public sealed record HealthUserBody
{
    /// <summary>CODIGO_PRESTADOR: the REPS code of the provider (or the one the Ministry assigned); required.</summary>
    [JsonPropertyName("provider_code")]
    public LegacyScalar? ProviderCode { get; init; }

    /// <summary>Identification type of the patient (1 to 13, ApiDian's health catalog); RIPS data, not written in the XML.</summary>
    [JsonPropertyName("health_type_document_identification_id")]
    public LegacyScalar? HealthTypeDocumentIdentificationId { get; init; }

    [JsonPropertyName("identification_number")]
    public LegacyScalar? IdentificationNumber { get; init; }

    [JsonPropertyName("surname")]
    public LegacyScalar? Surname { get; init; }

    [JsonPropertyName("second_surname")]
    public LegacyScalar? SecondSurname { get; init; }

    [JsonPropertyName("first_name")]
    public LegacyScalar? FirstName { get; init; }

    [JsonPropertyName("middle_name")]
    public LegacyScalar? MiddleName { get; init; }

    /// <summary>User type; RIPS data, not written in the XML.</summary>
    [JsonPropertyName("health_type_user_id")]
    public LegacyScalar? HealthTypeUserId { get; init; }

    /// <summary>MODALIDAD_PAGO: id of the SISPRO table <c>modalidadPago</c> (1 to 4); required.</summary>
    [JsonPropertyName("health_contracting_payment_method_id")]
    public LegacyScalar? HealthContractingPaymentMethodId { get; init; }

    /// <summary>COBERTURA_PLAN_BENEFICIOS: id of the SISPRO table <c>coberturaPlan</c> (1 to 17); required.</summary>
    [JsonPropertyName("health_coverage_id")]
    public LegacyScalar? HealthCoverageId { get; init; }

    /// <summary>Authorization numbers; RIPS data, not written in the XML. The name keeps the collection's spelling.</summary>
    [JsonPropertyName("autorization_numbers")]
    public LegacyScalar? AutorizationNumbers { get; init; }

    [JsonPropertyName("mipres")]
    public LegacyScalar? Mipres { get; init; }

    [JsonPropertyName("mipres_delivery")]
    public LegacyScalar? MipresDelivery { get; init; }

    /// <summary>NUMERO_CONTRATO (the CUCON); empty when there is no contract. Not together with <c>policy_number</c>.</summary>
    [JsonPropertyName("contract_number")]
    public LegacyScalar? ContractNumber { get; init; }

    /// <summary>NUMERO_POLIZA (SOAT or voluntary plan); not together with <c>contract_number</c>.</summary>
    [JsonPropertyName("policy_number")]
    public LegacyScalar? PolicyNumber { get; init; }

    /// <summary>FACTURA_SIN_CONTRATO: id of the SISPRO table <c>facturaSinContrato</c> (1 to 7), only without contract.</summary>
    [JsonPropertyName("health_invoice_without_contract_id")]
    public LegacyScalar? HealthInvoiceWithoutContractId { get; init; }

    [JsonPropertyName("co_payment")]
    public LegacyScalar? CoPayment { get; init; }

    [JsonPropertyName("moderating_fee")]
    public LegacyScalar? ModeratingFee { get; init; }

    [JsonPropertyName("recovery_fee")]
    public LegacyScalar? RecoveryFee { get; init; }

    [JsonPropertyName("shared_payment")]
    public LegacyScalar? SharedPayment { get; init; }

    [JsonPropertyName("advance_payment")]
    public LegacyScalar? AdvancePayment { get; init; }
}

/// <summary>Delivery terms of an export invoice (<c>deliveryterms</c>, cac:DeliveryTerms of the XML).</summary>
public sealed record DeliveryTermsBody
{
    /// <summary>How the transport costs are paid (cbc:SpecialTerms), free text.</summary>
    [JsonPropertyName("special_terms")]
    public LegacyScalar? SpecialTerms { get; init; }

    /// <summary>
    /// Incoterm (cbc:LossRiskResponsibilityCode): one of CFR, CIF, CIP, CPT, DAP, DAT, DDP, EXW, FAS, FCA or FOB.
    /// </summary>
    [JsonPropertyName("loss_risk_responsibility_code")]
    public LegacyScalar? LossRiskResponsibilityCode { get; init; }

    /// <summary>Free text the DIAN does not use (cbc:LossRisk).</summary>
    [JsonPropertyName("loss_risk")]
    public LegacyScalar? LossRisk { get; init; }
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
    /// Health-sector invoice only (required there): the collection concept, id of <c>prepaid_payment_types</c> (1 copago,
    /// 2 cuota moderadora, 3 pagos compartidos, 4 anticipo, 5 no aplica), written as the schemeID of cbc:ID. Other invoices
    /// ignore it, as ApiDian's <c>_prepaid_payment.blade.php</c>.
    /// </summary>
    [JsonPropertyName("prepaid_payment_type_id")]
    public LegacyScalar? PrepaidPaymentTypeId { get; init; }

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

    /// <summary>
    /// Export invoice and support-document adjustment note only: brand of the item (cbc:BrandName); the other documents
    /// ignore it.
    /// </summary>
    [JsonPropertyName("brandname")]
    public LegacyScalar? BrandName { get; init; }

    /// <summary>Export invoice and support-document adjustment note only: model of the item (cbc:ModelName).</summary>
    [JsonPropertyName("modelname")]
    public LegacyScalar? ModelName { get; init; }

    /// <summary>
    /// Support document only (required there): how the purchase is reported, from <c>type_generation_transmitions</c>
    /// (1 per operation, 2 weekly accumulated).
    /// </summary>
    [JsonPropertyName("type_generation_transmition_id")]
    public LegacyScalar? TypeGenerationTransmitionId { get; init; }

    /// <summary>Support document only (required there): date of the purchase, <c>yyyy-MM-dd</c>.</summary>
    [JsonPropertyName("start_date")]
    public LegacyScalar? StartDate { get; init; }

    /// <summary>Mandate invoice only: NIT (1 to 15 digits) of the third party on whose behalf the line is sold.</summary>
    [JsonPropertyName("agentparty")]
    public LegacyScalar? AgentParty { get; init; }

    /// <summary>Mandate invoice only: check digit of <c>agentparty</c>; required with it.</summary>
    [JsonPropertyName("agentparty_dv")]
    public LegacyScalar? AgentPartyDv { get; init; }

    /// <summary>
    /// Freight transport invoice only (required there): true when the line is a consignment registered in the RNDC
    /// (Registro Nacional de Despachos de Carga); then the five consignment fields are required.
    /// </summary>
    [JsonPropertyName("is_RNDC")]
    public LegacyScalar? IsRndc { get; init; }

    /// <summary>Consignment number in the RNDC (numeric).</summary>
    [JsonPropertyName("RNDC_consignment_number")]
    public LegacyScalar? RndcConsignmentNumber { get; init; }

    /// <summary>Internal consignment number of the carrier.</summary>
    [JsonPropertyName("internal_consignment_number")]
    public LegacyScalar? InternalConsignmentNumber { get; init; }

    /// <summary>Consignment value (integer).</summary>
    [JsonPropertyName("value_consignment")]
    public LegacyScalar? ValueConsignment { get; init; }

    /// <summary>Unit (<c>unit_measures</c> catalog) of the transported quantity.</summary>
    [JsonPropertyName("unit_measure_consignment_id")]
    public LegacyScalar? UnitMeasureConsignmentId { get; init; }

    /// <summary>Transported quantity (numeric); it goes in the XML as it arrives.</summary>
    [JsonPropertyName("quantity_consignment")]
    public LegacyScalar? QuantityConsignment { get; init; }
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
