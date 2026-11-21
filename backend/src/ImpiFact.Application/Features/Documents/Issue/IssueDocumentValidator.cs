using System.Globalization;
using FluentValidation;
using FluentValidation.Results;
using ImpiFact.Dian.Ubl;

namespace ImpiFact.Application.Features.Documents.Issue;

/// <summary>
/// Validación de forma de la emisión heredada, con las reglas de <c>InvoiceRequest</c>, <c>CreditNoteRequest</c> y
/// <c>DebitNoteRequest</c> del PHP (php-rules, secciones 3.1 y 4.1) y sus mensajes. Los errores llevan la ruta JSON de
/// Laravel (<c>invoice_lines.0.description</c>). Lo que depende de la base (catálogos, resolución, rango del número,
/// vigencia, totales) lo revisa el caso de uso.
/// </summary>
/// <remarks>
/// Diferencias con el PHP, a propósito: <c>number</c> es opcional (supuesto B2: sin número, lo asigna el
/// consecutivo); los campos de <c>tax_totals</c> son obligatorios siempre (el PHP los pedía solo con
/// <c>allowance_charges</c>, un error); y los textos tienen el largo máximo de sus columnas, para no fallar al guardar.
/// </remarks>
internal static class IssueDocumentRules
{
    /// <summary>Impuestos que no llevan porcentaje (<c>required_unless:tax_id,10,2</c>).</summary>
    private static readonly int[] TaxesWithoutPercent = [2, 10];

    /// <summary>Impuesto que exige unidad, valor por unidad y unidades (<c>required_if:tax_id,10</c>).</summary>
    private const int PerUnitTax = 10;

    public const string FinalConsumer = "222222222222";

    public static void Validate(IssueSpec spec, IssueDocumentBody body, Action<string, string> fail)
    {
        var check = new Checker(fail);

        if (check.RequiredInteger("type_document_id", body.TypeDocumentId) is { } typeId && typeId != spec.DocumentTypeId)
        {
            check.Fail("type_document_id", IssueMessages.Invalid("type_document_id"));
        }

        check.Integer("number", body.Number);
        check.Date("date", body.Date);
        check.Time("time", body.Time);
        check.Text("prefix", body.Prefix, 10);
        check.Text("resolution_number", body.ResolutionNumber, 30);
        check.Text("notes", body.Notes, 2000);
        check.Text("head_note", body.HeadNote, 2000);
        check.Text("foot_note", body.FootNote, 2000);
        check.Text("discrepancyresponsedescription", body.DiscrepancyResponseDescription, 500);
        var operation = check.Integer("type_operation_id", body.TypeOperationId);
        if (operation is { } operationId && spec.AllowedOperationTypeIds is { } allowed && !allowed.Contains((int)operationId))
        {
            check.Fail("type_operation_id", IssueMessages.Invalid("type_operation_id"));
        }

        check.Integer("idcurrency", body.IdCurrency);
        check.Decimal("calculationrate", body.CalculationRate);
        check.Date("calculationratedate", body.CalculationRateDate);

        ValidateCustomer(spec, check, body.Customer);
        ValidatePayment(check, body);
        ValidatePrepaidPayments(spec, check, body);
        ValidateAllowanceCharges(check, "allowance_charges", body.AllowanceCharges, isLine: false);
        ValidateTaxes(check, "tax_totals", body.TaxTotals);
        ValidateWithholdings(check, body.WithHoldingTaxTotal);
        ValidateTotals(spec, check, spec.TotalsOf(body));
        ValidateLines(spec, check, spec.LinesOf(body));
        ValidateVariant(spec, check, body);

        if (spec.IsNote)
        {
            ValidateNote(spec, check, body, operation);
        }
    }

    private static void ValidateCustomer(IssueSpec spec, Checker check, CustomerBody? customer)
    {
        if (customer is null)
        {
            check.Fail("customer", IssueMessages.Required("customer"));
            return;
        }

        var identification = check.RequiredText("customer.identification_number", customer.IdentificationNumber);
        if (identification is not null)
        {
            if (!identification.All(char.IsAsciiLetterOrDigit))
            {
                check.Fail("customer.identification_number", IssueMessages.AlphaNum("customer.identification_number"));
            }
            else if (identification.Length > 15)
            {
                check.Fail("customer.identification_number", IssueMessages.BetweenLength("customer.identification_number", 1, 15));
            }
        }

        var identificationType = check.Integer("customer.type_document_identification_id", customer.TypeDocumentIdentificationId);
        // Laravel's «required» treats only null and blank text as missing: a check digit of 0 is a value.
        if (!LegacyScalar.IsMissing(customer.Dv) && customer.Dv is { } dv)
        {
            if (!dv.TryInteger(out _) || dv.Text.Trim().Length != 1)
            {
                check.Fail("customer.dv", IssueMessages.Digits("customer.dv", 1));
            }
            else if (identification is not null && identification.All(char.IsAsciiDigit)
                && DianCheckDigit.Of(identification) != int.Parse(dv.Text.Trim(), CultureInfo.InvariantCulture))
            {
                check.Fail("customer.dv", IssueMessages.InvalidDv);
            }
        }
        else if (identificationType == 6 && spec.Kind == IssueKind.Invoice)
        {
            check.Fail("customer.dv", IssueMessages.RequiredIf("customer.dv", "customer.type_document_identification_id", "6"));
        }

        check.Integer("customer.type_organization_id", customer.TypeOrganizationId);
        check.Integer("customer.type_regime_id", customer.TypeRegimeId);
        check.Integer("customer.type_liability_id", customer.TypeLiabilityId);
        check.Integer("customer.tax_id", customer.TaxId);
        check.Integer("customer.language_id", customer.LanguageId);
        check.Integer("customer.country_id", customer.CountryId);
        check.Integer("customer.municipality_id", customer.MunicipalityId);
        check.RequiredText("customer.name", customer.Name, 300);
        check.Text("customer.phone", customer.Phone, 20);
        check.Text("customer.address", customer.Address, 300);
        check.Text("customer.merchant_registration", customer.MerchantRegistration, 50);

        if (check.Text("customer.email", customer.Email, 200) is { Length: > 0 } email)
        {
            if (!IssueMessages.IsEmail(email))
            {
                check.Fail("customer.email", IssueMessages.Email("customer.email"));
            }
        }
        else if (spec.Kind == IssueKind.DebitNote && identification != FinalConsumer)
        {
            check.Fail("customer.email", IssueMessages.RequiredUnless("customer.email", "customer.identification_number", FinalConsumer));
        }
    }

    private static void ValidatePayment(Checker check, IssueDocumentBody body)
    {
        if (body.PaymentForm is not { } forms)
        {
            return;
        }

        // Varias formas de pago, como el PHP: cada una es un cac:PaymentMeans del XML.
        foreach (var (form, index) in forms.Select((form, index) => (form, index)))
        {
            var path = forms.Count == 1 ? "payment_form" : $"payment_form.{index}";
            var formId = check.Integer($"{path}.payment_form_id", form.PaymentFormId);
            check.Integer($"{path}.payment_method_id", form.PaymentMethodId);
            var due = check.Date($"{path}.payment_due_date", form.PaymentDueDate);
            if (form.DurationMeasure is { IsPhpEmpty: false } duration && (!duration.TryInteger(out var days) || days < 0 || days > 999))
            {
                check.Fail($"{path}.duration_measure", IssueMessages.DigitsBetween($"{path}.duration_measure", 1, 3));
            }

            if (formId == 2)
            {
                if (form.PaymentDueDate is null)
                {
                    check.Fail($"{path}.payment_due_date", IssueMessages.RequiredIf($"{path}.payment_due_date", $"{path}.payment_form_id", "2"));
                }
                else if (due is { } dueDate && IsDate(body.Date, out var issueDate) && dueDate < issueDate)
                {
                    check.Fail($"{path}.payment_due_date", IssueMessages.AfterOrEqual($"{path}.payment_due_date", "date"));
                }

                if (form.DurationMeasure is null)
                {
                    check.Fail($"{path}.duration_measure", IssueMessages.RequiredIf($"{path}.duration_measure", $"{path}.payment_form_id", "2"));
                }
            }
        }
    }

    /// <summary>
    /// Anticipos de la factura (<c>prepaid_payment</c> y <c>prepaid_payments</c>). El PHP los dejaba todos opcionales y
    /// escribía elementos vacíos que la DIAN rechaza; aquí el ID, el valor y la fecha de recibido son obligatorios
    /// (reglas FBD02, FBD03 y FBD05 del anexo técnico). Como el PHP, <c>pre_paid_amount</c> es obligatorio cuando
    /// llega <c>prepaid_payment</c> (<c>required_with</c>).
    /// </summary>
    private static void ValidatePrepaidPayments(IssueSpec spec, Checker check, IssueDocumentBody body)
    {
        foreach (var (path, prepaid) in LegacyDocumentMapper.PrepaidPaymentsOf(spec, body))
        {
            check.RequiredText($"{path}.idpayment", prepaid.IdPayment, 150);
            check.RequiredDecimal($"{path}.paidamount", prepaid.PaidAmount);
            if (LegacyScalar.IsMissing(prepaid.ReceivedDate))
            {
                check.Fail($"{path}.receiveddate", IssueMessages.Required($"{path}.receiveddate"));
            }
            else
            {
                check.Date($"{path}.receiveddate", prepaid.ReceivedDate);
            }

            check.Date($"{path}.paiddate", prepaid.PaidDate);
            check.Text($"{path}.instructionid", prepaid.InstructionId, 500);
        }

        if (spec.Kind == IssueKind.Invoice && body.PrepaidPayment is { IsEmpty: false }
            && spec.TotalsOf(body) is { } totals && LegacyScalar.IsMissing(totals.PrePaidAmount))
        {
            var field = $"{spec.TotalsField}.pre_paid_amount";
            check.Fail(field, IssueMessages.RequiredWith(field, "prepaid_payment"));
        }
    }

    private static void ValidateAllowanceCharges(Checker check, string path, List<AllowanceChargeBody>? items, bool isLine)
    {
        foreach (var (item, index) in (items ?? []).Select((item, index) => (item, index)))
        {
            var at = $"{path}.{index}";
            var isCharge = check.RequiredBoolean($"{at}.charge_indicator", item.ChargeIndicator);
            check.Integer($"{at}.discount_id", item.DiscountId);
            check.RequiredText($"{at}.allowance_charge_reason", item.AllowanceChargeReason, 500);
            check.RequiredDecimal($"{at}.amount", item.Amount);
            check.Percentage($"{at}.multiplier_factor_numeric", item.MultiplierFactorNumeric);
            if (!isLine || isCharge == false)
            {
                check.RequiredDecimal($"{at}.base_amount", item.BaseAmount);
            }
            else
            {
                check.Decimal($"{at}.base_amount", item.BaseAmount);
            }

            if (isLine && isCharge == true && item.MultiplierFactorNumeric is null)
            {
                check.Fail($"{at}.multiplier_factor_numeric", IssueMessages.RequiredIf($"{at}.multiplier_factor_numeric", $"{at}.charge_indicator", "true"));
            }
        }
    }

    private static void ValidateTaxes(Checker check, string path, List<TaxTotalBody>? items)
    {
        foreach (var (item, index) in (items ?? []).Select((item, index) => (item, index)))
        {
            var at = $"{path}.{index}";
            var taxId = check.RequiredInteger($"{at}.tax_id", item.TaxId);
            check.RequiredDecimal($"{at}.tax_amount", item.TaxAmount);
            check.RequiredDecimal($"{at}.taxable_amount", item.TaxableAmount);
            if (taxId is { } id && TaxesWithoutPercent.Contains((int)id))
            {
                check.Percentage($"{at}.percent", item.Percent);
            }
            else if (item.Percent is null)
            {
                check.Fail($"{at}.percent", IssueMessages.RequiredUnless($"{at}.percent", $"{at}.tax_id", "10, 2"));
            }
            else
            {
                check.Percentage($"{at}.percent", item.Percent);
            }

            check.Integer($"{at}.unit_measure_id", item.UnitMeasureId);
            check.Decimal($"{at}.per_unit_amount", item.PerUnitAmount);
            check.Decimal($"{at}.base_unit_measure", item.BaseUnitMeasure);
            if (taxId == PerUnitTax)
            {
                foreach (var (field, value) in new[] { ("unit_measure_id", item.UnitMeasureId), ("per_unit_amount", item.PerUnitAmount), ("base_unit_measure", item.BaseUnitMeasure) })
                {
                    if (LegacyScalar.IsMissing(value))
                    {
                        check.Fail($"{at}.{field}", IssueMessages.RequiredIf($"{at}.{field}", $"{at}.tax_id", "10"));
                    }
                }
            }
        }
    }

    private static void ValidateWithholdings(Checker check, List<TaxTotalBody>? items)
    {
        foreach (var (item, index) in (items ?? []).Select((item, index) => (item, index)))
        {
            var at = $"with_holding_tax_total.{index}";
            check.RequiredInteger($"{at}.tax_id", item.TaxId);
            check.RequiredDecimal($"{at}.tax_amount", item.TaxAmount);
            check.RequiredDecimal($"{at}.taxable_amount", item.TaxableAmount);
            check.Percentage($"{at}.percent", item.Percent);
            check.Integer($"{at}.unit_measure_id", item.UnitMeasureId);
            check.Decimal($"{at}.per_unit_amount", item.PerUnitAmount);
            check.Decimal($"{at}.base_unit_measure", item.BaseUnitMeasure);
        }
    }

    private static void ValidateTotals(IssueSpec spec, Checker check, MonetaryTotalsBody? totals)
    {
        var path = spec.TotalsField;
        if (totals is null)
        {
            check.Fail(path, IssueMessages.Required(path));
            return;
        }

        check.RequiredDecimal($"{path}.line_extension_amount", totals.LineExtensionAmount);
        check.RequiredDecimal($"{path}.tax_exclusive_amount", totals.TaxExclusiveAmount);
        check.RequiredDecimal($"{path}.tax_inclusive_amount", totals.TaxInclusiveAmount);
        check.Decimal($"{path}.allowance_total_amount", totals.AllowanceTotalAmount);
        check.Decimal($"{path}.charge_total_amount", totals.ChargeTotalAmount);
        check.Decimal($"{path}.pre_paid_amount", totals.PrePaidAmount);
        check.RequiredDecimal($"{path}.payable_amount", totals.PayableAmount);
    }

    private static void ValidateLines(IssueSpec spec, Checker check, List<LineBody>? lines)
    {
        var path = spec.LinesField;
        if (lines is not { Count: > 0 })
        {
            check.Fail(path, IssueMessages.Required(path));
            return;
        }

        foreach (var (line, index) in lines.Select((line, index) => (line, index)))
        {
            var at = $"{path}.{index}";
            check.RequiredInteger($"{at}.unit_measure_id", line.UnitMeasureId);
            check.RequiredDecimal($"{at}.invoiced_quantity", line.InvoicedQuantity);
            check.RequiredDecimal($"{at}.line_extension_amount", line.LineExtensionAmount);
            var free = check.RequiredBoolean($"{at}.free_of_charge_indicator", line.FreeOfChargeIndicator);
            if (check.Integer($"{at}.reference_price_id", line.ReferencePriceId) is null && free == true)
            {
                check.Fail($"{at}.reference_price_id", IssueMessages.RequiredIf($"{at}.reference_price_id", $"{at}.free_of_charge_indicator", "true"));
            }

            check.RequiredText($"{at}.description", line.Description, 500);
            check.Text($"{at}.notes", line.Notes, 1000);
            check.RequiredText($"{at}.code", line.Code, 100);
            check.RequiredInteger($"{at}.type_item_identification_id", line.TypeItemIdentificationId);
            check.RequiredDecimal($"{at}.price_amount", line.PriceAmount);
            check.RequiredDecimal($"{at}.base_quantity", line.BaseQuantity);
            ValidateAllowanceCharges(check, $"{at}.allowance_charges", line.AllowanceCharges, isLine: true);
            ValidateTaxes(check, $"{at}.tax_totals", line.TaxTotals);
        }
    }

    /// <summary>
    /// Fields of the AIU, mandate and freight transport invoices (php-rules, section 10). The other rules are those of
    /// the standard invoice, also for the customer (php-rules 9.2, item 5). Differences with the PHP, on purpose: the
    /// mandator's check digit is required with its NIT and must be the DIAN one for that NIT, and the RNDC numbers must
    /// be digits.
    /// </summary>
    private static void ValidateVariant(IssueSpec spec, Checker check, IssueDocumentBody body)
    {
        if (spec.Variant == InvoiceVariant.Aiu)
        {
            check.RequiredText("noteAIU", body.NoteAiu, 2000);
            return;
        }

        if (spec.Variant == InvoiceVariant.Export)
        {
            ValidateExport(spec, check, body);
            return;
        }

        foreach (var (line, index) in (spec.LinesOf(body) ?? []).Select((line, index) => (line, index)))
        {
            var at = $"{spec.LinesField}.{index}";
            if (spec.Variant == InvoiceVariant.Mandate)
            {
                ValidateMandator(check, at, line);
            }
            else if (spec.Variant == InvoiceVariant.Transport && check.RequiredBoolean($"{at}.is_RNDC", line.IsRndc) == true)
            {
                ValidateConsignment(check, at, line);
            }
        }
    }

    /// <summary>
    /// Fields of the export invoice (php-rules, section 11): <c>deliveryterms</c> with an Incoterm of the PHP list (the
    /// <c>incoterms</c> catalog without DPU, which the PHP rejected; open question 3 of section 18), the line brand and
    /// model, the city and state of a foreign customer, and <c>k_supplement</c>, optional but complete when sent (the PHP
    /// answered 500 to an incomplete one). A positive exchange rate; whether it is required depends on the currency
    /// catalog (<see cref="CatalogNeeds.ExchangeRateGaps"/>).
    /// </summary>
    private static void ValidateExport(IssueSpec spec, Checker check, IssueDocumentBody body)
    {
        if (body.DeliveryTerms is not { } terms)
        {
            check.Fail("deliveryterms", IssueMessages.Required("deliveryterms"));
        }
        else
        {
            check.RequiredText("deliveryterms.special_terms", terms.SpecialTerms, 500);
            if (check.RequiredText("deliveryterms.loss_risk_responsibility_code", terms.LossRiskResponsibilityCode, 10) is { } code
                && !ExportIncoterms.Accepted.Contains(code))
            {
                check.Fail("deliveryterms.loss_risk_responsibility_code", IssueMessages.Incoterm("deliveryterms.loss_risk_responsibility_code"));
            }

            check.RequiredText("deliveryterms.loss_risk", terms.LossRisk, 500);
        }

        if (check.Decimal("calculationrate", body.CalculationRate) is <= 0m)
        {
            check.Fail("calculationrate", IssueMessages.Positive("calculationrate"));
        }

        if (body.Customer is { } customer)
        {
            check.Text("customer.municipality_name", customer.MunicipalityName, 100);
            check.Text("customer.state_name", customer.StateName, 100);
        }

        foreach (var (line, index) in (spec.LinesOf(body) ?? []).Select((line, index) => (line, index)))
        {
            check.Text($"{spec.LinesField}.{index}.brandname", line.BrandName, 300);
            check.Text($"{spec.LinesField}.{index}.modelname", line.ModelName, 300);
        }

        if (body.KSupplement is not { } supplement)
        {
            return;
        }

        // Annex 1.9, section 21: every value of the supplement K is text of up to 100 characters.
        var keys = ExportSupplement.AdditionalInformationFields.Select(field => field.Key).Concat(ExportSupplement.CopTotalFields);
        foreach (var key in keys)
        {
            var field = $"k_supplement.{key}";
            var value = supplement.GetValueOrDefault(key);
            if (LegacyScalar.IsMissing(value))
            {
                check.Fail(field, IssueMessages.RequiredWith(field, "k_supplement"));
            }
            else if (value!.Value.Kind is LegacyScalarKind.True or LegacyScalarKind.False)
            {
                check.Fail(field, IssueMessages.Invalid(field));
            }
            else
            {
                check.Text(field, value, 100);
            }
        }

        // FctConvCop is the exchange rate to pesos: ImpiFact never fetches nor invents it (the PHP read the DIAN page or
        // wrote 4000.00), so it must be a positive number.
        if (supplement.GetValueOrDefault("FctConvCop") is { Kind: not (LegacyScalarKind.True or LegacyScalarKind.False) } rate
            && !LegacyScalar.IsMissing(rate) && check.Decimal("k_supplement.FctConvCop", rate) is <= 0m)
        {
            check.Fail("k_supplement.FctConvCop", IssueMessages.Positive("k_supplement.FctConvCop"));
        }
    }

    /// <summary>
    /// <c>agentparty</c> (<c>digits_between:1,15</c>) and <c>agentparty_dv</c> (<c>digits:1</c>), both optional as in
    /// the PHP, but the check digit goes with the NIT.
    /// </summary>
    private static void ValidateMandator(Checker check, string at, LineBody line)
    {
        string? nit = null;
        if (!LegacyScalar.IsMissing(line.AgentParty))
        {
            var text = line.AgentParty!.Value.Text.Trim();
            if (text.Length is 0 or > 15 || !text.All(char.IsAsciiDigit) || line.AgentParty.Value.Kind is LegacyScalarKind.True or LegacyScalarKind.False)
            {
                check.Fail($"{at}.agentparty", IssueMessages.DigitsBetween($"{at}.agentparty", 1, 15));
            }
            else
            {
                nit = text;
            }
        }

        if (LegacyScalar.IsMissing(line.AgentPartyDv))
        {
            if (!LegacyScalar.IsMissing(line.AgentParty))
            {
                check.Fail($"{at}.agentparty_dv", IssueMessages.RequiredWith($"{at}.agentparty_dv", $"{at}.agentparty"));
            }

            return;
        }

        var dv = line.AgentPartyDv!.Value;
        if (!dv.TryInteger(out var digit) || dv.Text.Trim().Length != 1)
        {
            check.Fail($"{at}.agentparty_dv", IssueMessages.Digits($"{at}.agentparty_dv", 1));
        }
        else if (nit is not null && DianCheckDigit.Of(nit) != digit)
        {
            check.Fail($"{at}.agentparty_dv", IssueMessages.InvalidDv);
        }
    }

    /// <summary>The five consignment fields that <c>is_RNDC = true</c> requires (<c>required_if:...is_RNDC,true</c>).</summary>
    private static void ValidateConsignment(Checker check, string at, LineBody line)
    {
        string Field(string name) => $"{at}.{name}";
        void Required(string name, LegacyScalar? value, Func<LegacyScalar, string?> invalid)
        {
            if (LegacyScalar.IsMissing(value) || value!.Value.Kind is LegacyScalarKind.True or LegacyScalarKind.False)
            {
                check.Fail(Field(name), LegacyScalar.IsMissing(value)
                    ? IssueMessages.RequiredIf(Field(name), Field("is_RNDC"), "true")
                    : IssueMessages.Invalid(Field(name)));
            }
            else if (invalid(value.Value) is { } message)
            {
                check.Fail(Field(name), message);
            }
        }

        Required("RNDC_consignment_number", line.RndcConsignmentNumber, value =>
            value.Text.Trim() is not { Length: > 0 } digits || !digits.All(char.IsAsciiDigit) ? IssueMessages.Numeric(Field("RNDC_consignment_number"))
            : value.Text.Trim().Length > 30 ? IssueMessages.MaxLength(Field("RNDC_consignment_number"), 30) : null);
        Required("internal_consignment_number", line.InternalConsignmentNumber, value =>
            value.Text.Length > 100 ? IssueMessages.MaxLength(Field("internal_consignment_number"), 100) : null);
        Required("value_consignment", line.ValueConsignment, value =>
            !value.TryInteger(out var amount) ? IssueMessages.Integer(Field("value_consignment"))
            : Math.Abs(amount) > LegacyScalar.MaxMagnitude ? IssueMessages.MaxNumber(Field("value_consignment"), LegacyScalar.MaxMagnitude) : null);
        Required("unit_measure_consignment_id", line.UnitMeasureConsignmentId, value =>
            value.TryInteger(out var id) && id is > 0 and <= int.MaxValue ? null : IssueMessages.Integer(Field("unit_measure_consignment_id")));
        Required("quantity_consignment", line.QuantityConsignment, value =>
            !value.TryDecimal(out var quantity) ? IssueMessages.Numeric(Field("quantity_consignment"))
            : Math.Abs(quantity) > LegacyScalar.MaxMagnitude ? IssueMessages.MaxNumber(Field("quantity_consignment"), LegacyScalar.MaxMagnitude) : null);
    }

    private static void ValidateNote(IssueSpec spec, Checker check, IssueDocumentBody body, long? operation)
    {
        if (check.Integer("discrepancyresponsecode", body.DiscrepancyResponseCode) is { } code && (code < 1 || code > spec.MaxDiscrepancyCode))
        {
            check.Fail("discrepancyresponsecode", IssueMessages.Between("discrepancyresponsecode", 1, spec.MaxDiscrepancyCode!.Value));
        }

        if (body.BillingReference is { } reference)
        {
            check.RequiredText("billing_reference.number", reference.Number, 30);
            if (check.RequiredText("billing_reference.uuid", reference.Uuid) is { } uuid && uuid.Length != 96)
            {
                check.Fail("billing_reference.uuid", IssueMessages.Size("billing_reference.uuid", 96));
            }

            if (LegacyScalar.IsMissing(reference.IssueDate))
            {
                check.Fail("billing_reference.issue_date", IssueMessages.RequiredWith("billing_reference.issue_date", "billing_reference"));
            }
            else
            {
                check.Date("billing_reference.issue_date", reference.IssueDate);
            }
        }

        if (body.InvoicePeriod is { } period)
        {
            foreach (var (field, value) in new[] { ("start_date", period.StartDate), ("end_date", period.EndDate) })
            {
                if (LegacyScalar.IsMissing(value))
                {
                    check.Fail($"invoice_period.{field}", IssueMessages.RequiredWith($"invoice_period.{field}", "invoice_period"));
                }
                else
                {
                    check.Date($"invoice_period.{field}", value);
                }
            }
        }
        else if (operation == spec.OperationWithoutReferenceId)
        {
            check.Fail("invoice_period", IssueMessages.RequiredIf("invoice_period", "type_operation_id", spec.OperationWithoutReferenceId!.Value.ToString(CultureInfo.InvariantCulture)));
        }
    }

    private static bool IsDate(LegacyScalar? value, out DateOnly date)
    {
        date = default;
        return value is { } scalar && DateOnly.TryParseExact(scalar.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    /// <summary>Agrega fallas con la ruta del campo y devuelve el valor leído cuando es válido.</summary>
    private sealed class Checker(Action<string, string> fail)
    {
        private const decimal MaxPercentage = 99_999m;

        public void Fail(string field, string message) => fail(field, message);

        public long? Integer(string field, LegacyScalar? value)
        {
            if (value is not { } scalar || (scalar.Kind == LegacyScalarKind.String && scalar.Text.Length == 0))
            {
                return null;
            }

            if (scalar.TryInteger(out var number))
            {
                // Los ids de catálogo y los códigos caben en un entero de 32 bits; el número del documento no.
                if (field != "number" && (number > int.MaxValue || number < int.MinValue))
                {
                    Fail(field, IssueMessages.Invalid(field));
                    return null;
                }

                return number;
            }

            Fail(field, IssueMessages.Integer(field));
            return null;
        }

        public long? RequiredInteger(string field, LegacyScalar? value)
        {
            if (value is null || value.Value.Text.Length == 0)
            {
                Fail(field, IssueMessages.Required(field));
                return null;
            }

            return Integer(field, value);
        }

        public decimal? Decimal(string field, LegacyScalar? value)
        {
            if (value is not { } scalar || (scalar.Kind == LegacyScalarKind.String && scalar.Text.Length == 0))
            {
                return null;
            }

            if (!scalar.TryDecimal(out var number))
            {
                Fail(field, IssueMessages.Numeric(field));
                return null;
            }

            if (Math.Abs(number) > LegacyScalar.MaxMagnitude)
            {
                Fail(field, IssueMessages.MaxNumber(field, LegacyScalar.MaxMagnitude));
                return null;
            }

            return number;
        }

        /// <summary>Porcentaje o factor: cabe en <c>numeric(9,4)</c>, así que no puede pasar de 99999.</summary>
        public void Percentage(string field, LegacyScalar? value)
        {
            if (Decimal(field, value) is { } number && Math.Abs(number) > MaxPercentage)
            {
                Fail(field, IssueMessages.MaxNumber(field, MaxPercentage));
            }
        }

        public decimal? RequiredDecimal(string field, LegacyScalar? value)
        {
            if (value is null || value.Value.Text.Length == 0)
            {
                Fail(field, IssueMessages.Required(field));
                return null;
            }

            return Decimal(field, value);
        }

        public bool? RequiredBoolean(string field, LegacyScalar? value)
        {
            if (value is null)
            {
                Fail(field, IssueMessages.Required(field));
                return null;
            }

            if (value.Value.TryBoolean(out var flag))
            {
                return flag;
            }

            Fail(field, IssueMessages.Boolean(field));
            return null;
        }

        public string? Text(string field, LegacyScalar? value, int maxLength)
        {
            if (value is not { } scalar || scalar.Kind is LegacyScalarKind.True or LegacyScalarKind.False)
            {
                return null;
            }

            if (scalar.Text.Length > maxLength)
            {
                Fail(field, IssueMessages.MaxLength(field, maxLength));
                return null;
            }

            return scalar.Text;
        }

        public string? RequiredText(string field, LegacyScalar? value, int maxLength = 1000)
        {
            if (value is null || string.IsNullOrWhiteSpace(value.Value.Text))
            {
                Fail(field, IssueMessages.Required(field));
                return null;
            }

            return Text(field, value, maxLength);
        }

        public DateOnly? Date(string field, LegacyScalar? value)
        {
            if (value is not { } scalar || (scalar.Kind == LegacyScalarKind.String && scalar.Text.Length == 0))
            {
                return null;
            }

            if (DateOnly.TryParseExact(scalar.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return date;
            }

            Fail(field, IssueMessages.DateFormat(field, "Y-m-d"));
            return null;
        }

        public void Time(string field, LegacyScalar? value)
        {
            if (value is { } scalar && !(scalar.Kind == LegacyScalarKind.String && scalar.Text.Length == 0)
                && !TimeOnly.TryParseExact(scalar.Text, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                Fail(field, IssueMessages.DateFormat(field, "H:i:s"));
            }
        }
    }
}

/// <summary>Dígito de verificación del NIT con el algoritmo de la DIAN (igual que <c>validarDigVerifDIAN</c> del PHP).</summary>
internal static class DianCheckDigit
{
    private static readonly int[] Weights = [3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71];

    /// <summary>El NIT debe tener solo dígitos y a lo sumo 15.</summary>
    public static int Of(string nit)
    {
        var sum = 0;
        for (var position = 0; position < nit.Length && position < Weights.Length; position++)
        {
            sum += (nit[nit.Length - 1 - position] - '0') * Weights[position];
        }

        var remainder = sum % 11;
        return remainder is 0 or 1 ? remainder : 11 - remainder;
    }
}

internal sealed class IssueInvoiceCommandValidator : AbstractValidator<IssueInvoiceCommand>
{
    public IssueInvoiceCommandValidator() =>
        RuleFor(command => command).Custom((command, context) =>
            IssueDocumentRules.Validate(IssueSpec.Invoice, command.Body, (field, message) => context.AddFailure(new ValidationFailure(field, message))));
}

internal sealed class IssueAiuInvoiceCommandValidator : AbstractValidator<IssueAiuInvoiceCommand>
{
    public IssueAiuInvoiceCommandValidator() =>
        RuleFor(command => command).Custom((command, context) =>
            IssueDocumentRules.Validate(IssueSpec.AiuInvoice, command.Body, (field, message) => context.AddFailure(new ValidationFailure(field, message))));
}

internal sealed class IssueMandateInvoiceCommandValidator : AbstractValidator<IssueMandateInvoiceCommand>
{
    public IssueMandateInvoiceCommandValidator() =>
        RuleFor(command => command).Custom((command, context) =>
            IssueDocumentRules.Validate(IssueSpec.MandateInvoice, command.Body, (field, message) => context.AddFailure(new ValidationFailure(field, message))));
}

internal sealed class IssueTransportInvoiceCommandValidator : AbstractValidator<IssueTransportInvoiceCommand>
{
    public IssueTransportInvoiceCommandValidator() =>
        RuleFor(command => command).Custom((command, context) =>
            IssueDocumentRules.Validate(IssueSpec.TransportInvoice, command.Body, (field, message) => context.AddFailure(new ValidationFailure(field, message))));
}

internal sealed class IssueExportInvoiceCommandValidator : AbstractValidator<IssueExportInvoiceCommand>
{
    public IssueExportInvoiceCommandValidator() =>
        RuleFor(command => command).Custom((command, context) =>
            IssueDocumentRules.Validate(IssueSpec.ExportInvoice, command.Body, (field, message) => context.AddFailure(new ValidationFailure(field, message))));
}

/// <summary>
/// Incoterms that the export invoice accepts: the list of the PHP validation (REQ/InvoiceExportRequest.php:231), which is
/// the seeded <c>incoterms</c> catalog without DPU. DPU (Incoterms 2020) replaced DAT, but the annex 1.9 list 13.3.7 is
/// in the DIAN toolbox and not in the repository, so it stays rejected until the habilitation tests confirm it
/// (php-rules, section 18, question 3).
/// </summary>
internal static class ExportIncoterms
{
    public static readonly IReadOnlySet<string> Accepted = new HashSet<string>(StringComparer.Ordinal)
    {
        "CFR", "CIF", "CIP", "CPT", "DAP", "DAT", "DDP", "EXW", "FAS", "FCA", "FOB",
    };
}

internal sealed class IssueCreditNoteCommandValidator : AbstractValidator<IssueCreditNoteCommand>
{
    public IssueCreditNoteCommandValidator() =>
        RuleFor(command => command).Custom((command, context) =>
            IssueDocumentRules.Validate(IssueSpec.CreditNote, command.Body, (field, message) => context.AddFailure(new ValidationFailure(field, message))));
}

internal sealed class IssueDebitNoteCommandValidator : AbstractValidator<IssueDebitNoteCommand>
{
    public IssueDebitNoteCommandValidator() =>
        RuleFor(command => command).Custom((command, context) =>
            IssueDocumentRules.Validate(IssueSpec.DebitNote, command.Body, (field, message) => context.AddFailure(new ValidationFailure(field, message))));
}
