using System.Globalization;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Totals;
using ImpiFact.Dian.Cufe;
using ImpiFact.Dian.Rounding;
using ImpiFact.Dian.Ubl;

namespace ImpiFact.Application.Features.Documents.Issue;

/// <summary>
/// Datos de la emisión que no vienen del cuerpo: la empresa, la resolución elegida, el número, la fecha y la hora en
/// Colombia, el PIN del software y la URL del QR del ambiente.
/// </summary>
internal sealed record IssueFrame(
    IssueSpec Spec,
    IssuerContext Issuer,
    IssuerResolution Resolution,
    long Number,
    DateOnly IssueDate,
    TimeOnly IssueTime,
    string SoftwarePin,
    string QrCodeBaseUrl)
{
    /// <summary>No incluye el PIN del software, para que un registro accidental del objeto no lo filtre.</summary>
    public override string ToString() => $"IssueFrame {{ Spec = {Spec}, Number = {Number}, IssueDate = {IssueDate} }}";
}

/// <summary>
/// Convierte el cuerpo heredado al modelo de <c>ImpiFact.Dian</c> (<see cref="SalesInvoice"/> o <see cref="SalesNote"/>)
/// con las mismas decisiones que tomaba el PHP (ApiDian) al llenar sus plantillas XML: valores por defecto de la
/// empresa y del cliente, valores opcionales que solo se escriben cuando PHP no los considera vacíos y textos sin
/// saltos de línea ni <c>|</c> (ver <see cref="PhpText"/>). Los adaptadores de las pruebas golden hacen lo mismo con
/// el contexto fijo de captura; este es el de producción.
/// </summary>
internal sealed class LegacyDocumentMapper(IReadOnlyDictionary<CatalogKey, CatalogItem> catalogs)
{
    /// <summary>
    /// Impuestos de valor fijo por unidad y no por porcentaje (<c>app/TaxTotal.php</c>, <c>is_fixed_value</c>).
    /// </summary>
    public static readonly int[] FixedValueTaxIds = [2, 10, 19, 20, 21, 23];

    // Valores por defecto del PHP para el cliente (withDefault de app/Company.php) y para el pago (DocumentTrait).
    public const int DefaultIdentificationTypeId = 3;
    public const int DefaultOrganizationTypeId = 2;
    public const int DefaultRegimeId = 2;
    public const int DefaultLiabilityId = 117;
    public const int DefaultTaxId = 1;
    public const int DefaultCountryId = 46;
    public const int DefaultLanguageId = 79;
    public const int DefaultPaymentFormId = 1;
    public const int DefaultPaymentMethodId = 10;

    public SalesInvoice MapInvoice(IssueFrame frame, IssueDocumentBody body)
    {
        var company = frame.Issuer.Company;
        var resolution = frame.Resolution;
        return new SalesInvoice
        {
            Environment = (DianEnvironment)frame.Issuer.TypeEnvironmentId,
            QrCodeBaseUrl = frame.QrCodeBaseUrl,
            OperationTypeCode = Get(DianCatalog.OperationType, OperationTypeId(frame.Spec, body)).Code,
            InvoiceTypeCode = Get(DianCatalog.DocumentType, frame.Spec.DocumentTypeId).Code,
            Note = PhpText.CleanOrNull(body.Notes),
            Resolution = new NumberingResolution(
                resolution.ResolutionNumber ?? "",
                resolution.ValidFrom ?? throw new InvalidOperationException("An invoice resolution needs a validity start."),
                resolution.ValidTo ?? throw new InvalidOperationException("An invoice resolution needs a validity end."),
                resolution.Prefix ?? "",
                resolution.From,
                resolution.To,
                resolution.TechnicalKey ?? throw new InvalidOperationException("An invoice resolution needs a technical key.")),
            Number = frame.Number,
            IssueDate = frame.IssueDate,
            IssueTime = frame.IssueTime,
            Software = new SoftwareCredentials(frame.Issuer.Software!.Identifier, frame.SoftwarePin),
            LocalCurrencyCode = Get(DianCatalog.Currency, company.TypeCurrencyId).Code,
            ForeignCurrency = MapForeignCurrency(body),
            Supplier = MapSupplier(frame.Issuer),
            Customer = MapCustomer(body.Customer!),
            Payments = PaymentFormsOf(body).Select(form => MapPayment(form.Body)).ToList(),
            PrepaidPayments = PrepaidPaymentsOf(frame.Spec, body).Select(prepaid => MapPrepaidPayment(prepaid.Body)).ToList(),
            AllowanceCharges = MapAll(body.AllowanceCharges, MapAllowanceCharge),
            TaxSubtotals = MapAll(body.TaxTotals, MapTaxSubtotal),
            WithholdingSubtotals = MapAll(body.WithHoldingTaxTotal, MapTaxSubtotal),
            Totals = MapTotals(frame.Spec.TotalsOf(body)!),
            Lines = MapAll(frame.Spec.LinesOf(body), line => MapInvoiceLine(frame.Spec.Variant, line)),
            AiuContractObject = frame.Spec.Variant == InvoiceVariant.Aiu ? PhpText.Clean(body.NoteAiu!.Value.Text) : null,
            LineIdScheme = frame.Spec.Variant switch
            {
                InvoiceVariant.Mandate => InvoiceLineIdScheme.Mandate,
                InvoiceVariant.Transport => InvoiceLineIdScheme.Transport,
                _ => InvoiceLineIdScheme.Plain,
            },
        };
    }

    /// <summary>
    /// The mandator only in the mandate invoice and the RNDC consignment only in the transport one (php-rules 10.2 and
    /// 10.3); their values go to the XML as they arrive, without line breaks or «|».
    /// </summary>
    private InvoiceLine MapInvoiceLine(InvoiceVariant variant, LineBody line) => MapLine(line) with
    {
        Mandator = variant == InvoiceVariant.Mandate && !LegacyScalar.IsMissing(line.AgentParty)
            ? new LineMandator(PhpText.Clean(line.AgentParty!.Value.Text.Trim()), PhpText.Clean(line.AgentPartyDv!.Value.Text.Trim()))
            : null,
        Consignment = variant == InvoiceVariant.Transport && line.IsRndc!.Value.Boolean
            ? new RndcConsignment(
                PhpText.Clean(line.RndcConsignmentNumber!.Value.Text.Trim()),
                PhpText.Clean(line.InternalConsignmentNumber!.Value.Text),
                PhpText.Clean(line.ValueConsignment!.Value.Text.Trim()),
                Get(DianCatalog.UnitMeasure, (int)line.UnitMeasureConsignmentId!.Value.Integer).Code,
                PhpText.Clean(line.QuantityConsignment!.Value.Text.Trim()))
            : null,
    };

    public SalesNote MapNote(IssueFrame frame, IssueDocumentBody body)
    {
        var spec = frame.Spec;
        return new SalesNote
        {
            Kind = spec.Kind == IssueKind.CreditNote ? NoteKind.Credit : NoteKind.Debit,
            Environment = (DianEnvironment)frame.Issuer.TypeEnvironmentId,
            QrCodeBaseUrl = frame.QrCodeBaseUrl,
            OperationTypeCode = Get(DianCatalog.OperationType, OperationTypeId(spec, body)).Code,
            Prefix = PhpText.Clean(frame.Resolution.Prefix ?? ""),
            Number = frame.Number,
            IssueDate = frame.IssueDate,
            IssueTime = frame.IssueTime,
            Note = PhpText.CleanOrNull(body.Notes),
            Software = new SoftwareCredentials(frame.Issuer.Software!.Identifier, frame.SoftwarePin),
            LocalCurrencyCode = Get(DianCatalog.Currency, frame.Issuer.Company.TypeCurrencyId).Code,
            ForeignCurrency = MapForeignCurrency(body),
            Supplier = MapSupplier(frame.Issuer),
            Customer = MapCustomer(body.Customer!),
            Payments = PaymentFormsOf(body).Select(form => MapPayment(form.Body)).ToList(),
            InvoicePeriod = body.InvoicePeriod is { StartDate: { } start, EndDate: { } end } ? new InvoicePeriod(Date(start), Date(end)) : null,
            Discrepancy = MapDiscrepancy(spec, body),
            BillingReference = body.BillingReference is { Number: { } number, Uuid: { } uuid, IssueDate: { } issued }
                ? new BillingReference(PhpText.Clean(number.Text), PhpText.Clean(uuid.Text), Date(issued))
                : null,
            AllowanceCharges = MapAll(body.AllowanceCharges, MapAllowanceCharge),
            TaxSubtotals = MapAll(body.TaxTotals, MapTaxSubtotal),
            WithholdingSubtotals = MapAll(body.WithHoldingTaxTotal, MapTaxSubtotal),
            Totals = MapTotals(spec.TotalsOf(body)!),
            Lines = MapAll(spec.LinesOf(body), MapLine),
        };
    }

    /// <summary>Entrada de <see cref="DianTotalsValidator"/> con los valores del cliente, en el orden de la solicitud.</summary>
    public static TotalsDocument MapTotalsDocument(IssueSpec spec, IssueDocumentBody body)
    {
        var totals = spec.TotalsOf(body)!;
        return new TotalsDocument
        {
            Kind = spec.TotalsKind,
            MonetaryTotals = new MonetaryTotalsInput
            {
                LineExtensionAmount = totals.LineExtensionAmount!.Value.Decimal,
                TaxExclusiveAmount = totals.TaxExclusiveAmount!.Value.Decimal,
                TaxInclusiveAmount = totals.TaxInclusiveAmount!.Value.Decimal,
                AllowanceTotalAmount = Truthy(totals.AllowanceTotalAmount),
                ChargeTotalAmount = Truthy(totals.ChargeTotalAmount),
                PrepaidAmount = Truthy(totals.PrePaidAmount),
                PayableAmount = totals.PayableAmount!.Value.Decimal,
            },
            TaxTotals = (body.TaxTotals ?? []).Select(TotalsTax).ToList(),
            AllowanceCharges = (body.AllowanceCharges ?? []).Select(TotalsCharge).ToList(),
            PrepaidPayments = PrepaidPaymentsOf(spec, body).Select(prepaid => new PrepaidPaymentInput(prepaid.Body.PaidAmount!.Value.Decimal, prepaid.Path)).ToList(),
            Lines = (spec.LinesOf(body) ?? []).Select(line => new TotalsLineInput
            {
                Quantity = line.InvoicedQuantity!.Value.Decimal,
                LineExtensionAmount = line.LineExtensionAmount!.Value.Decimal,
                PriceAmount = line.PriceAmount!.Value.Decimal,
                BaseQuantity = line.BaseQuantity!.Value.Decimal,
                AllowanceCharges = (line.AllowanceCharges ?? []).Select(TotalsCharge).ToList(),
                TaxTotals = (line.TaxTotals ?? []).Select(TotalsTax).ToList(),
            }).ToList(),
        };
    }

    /// <summary>
    /// Formas de pago en el orden de la solicitud, con la ruta JSON de cada una: <c>payment_form</c> cuando llega una
    /// sola (un objeto o una lista de un elemento) y <c>payment_form.{i}</c> en una lista de varias, como Laravel. Sin
    /// <c>payment_form</c> (o con una lista vacía) queda una sola, nula, que toma los valores por defecto del PHP
    /// (<see cref="DefaultPaymentFormId"/> y <see cref="DefaultPaymentMethodId"/>).
    /// </summary>
    public static IReadOnlyList<(string Path, PaymentFormBody? Body)> PaymentFormsOf(IssueDocumentBody body) =>
        body.PaymentForm is not { Count: > 0 } forms
            ? [("payment_form", null)]
            : forms.Select((form, index) => (forms.Count == 1 ? "payment_form" : $"payment_form.{index}", (PaymentFormBody?)form)).ToList();

    /// <summary>
    /// Anticipos que van en el XML, en el orden de la plantilla del PHP (<c>01.blade.php</c>): primero
    /// <c>prepaid_payment</c> si no está vacío y luego cada <c>prepaid_payments</c>, con la ruta JSON de cada uno para
    /// los errores. Las notas no llevan anticipos: sus plantillas no los escriben.
    /// </summary>
    public static IReadOnlyList<(string Path, PrepaidPaymentBody Body)> PrepaidPaymentsOf(IssueSpec spec, IssueDocumentBody body)
    {
        if (spec.Kind != IssueKind.Invoice)
        {
            return [];
        }

        var prepaid = new List<(string, PrepaidPaymentBody)>();
        if (body.PrepaidPayment is { IsEmpty: false } single)
        {
            prepaid.Add(("prepaid_payment", single));
        }

        prepaid.AddRange((body.PrepaidPayments ?? []).Select((item, index) => ($"prepaid_payments.{index}", item)));
        return prepaid;
    }

    /// <summary>Operación de la solicitud o la que el PHP usa por defecto en cada tipo.</summary>
    public static int OperationTypeId(IssueSpec spec, IssueDocumentBody body) =>
        body.TypeOperationId is { IsPhpEmpty: false } operation ? (int)operation.Integer : spec.DefaultOperationTypeId;

    /// <summary>Valor opcional solo si PHP no lo considera vacío (así lo escribe o lo omite la plantilla).</summary>
    public static decimal? Truthy(LegacyScalar? value) => value is { IsPhpEmpty: false } scalar ? scalar.Decimal : null;

    public static int IdOr(LegacyScalar? value, int fallback) => value is { IsPhpEmpty: false } scalar ? (int)scalar.Integer : fallback;

    public static int? IdOrNull(LegacyScalar? value) => value is { IsPhpEmpty: false } scalar ? (int)scalar.Integer : null;

    public static DateOnly Date(LegacyScalar value) => DateOnly.ParseExact(value.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private CatalogItem Get(DianCatalog catalog, int id) =>
        catalogs.TryGetValue(new CatalogKey(catalog, id), out var item)
            ? item
            : throw new KeyNotFoundException($"Catalog {catalog} has no ID {id}; the use case must check it first.");

    private static List<TOut> MapAll<TIn, TOut>(List<TIn>? items, Func<TIn, TOut> map) => (items ?? []).Select(map).ToList();

    private ForeignCurrency? MapForeignCurrency(IssueDocumentBody body)
    {
        if (IdOrNull(body.IdCurrency) is not { } currencyId)
        {
            return null;
        }

        return new ForeignCurrency(
            Get(DianCatalog.Currency, currencyId).Code,
            body.CalculationRate is { } rate && rate.Text.Length > 0 ? rate.Decimal : null,
            body.CalculationRateDate is { } date && date.Text.Length > 0 ? Date(date) : null);
    }

    private InvoiceParty MapSupplier(IssuerContext issuer)
    {
        var company = issuer.Company;
        var tax = Get(DianCatalog.Tax, company.TaxId);

        // Con varias responsabilidades, el PHP las escribe en orden de id separadas por «;» (type_liability_codes).
        var liabilities = string.Join(';', issuer.LiabilityIds.Order().Select(id => Get(DianCatalog.Liability, id).Code));
        return new InvoiceParty
        {
            OrganizationTypeCode = Get(DianCatalog.OrganizationType, company.TypeOrganizationId).Code,
            IdentificationNumber = PhpText.Clean(company.IdentificationNumber),
            CheckDigit = PhpText.Clean(company.Dv),
            IdentificationTypeCode = Get(DianCatalog.IdentificationType, company.TypeDocumentIdentificationId).Code,
            Name = PhpText.Clean(company.Name),
            Address = MapAddress(company.MunicipalityId, company.Address, company.CountryId, company.LanguageId),
            RegimeCode = Get(DianCatalog.Regime, company.TypeRegimeId).Code,
            LiabilityCode = liabilities,
            TaxSchemeCode = tax.Code,
            TaxSchemeName = tax.Name,
            MerchantRegistration = PhpText.Clean(company.MerchantRegistration ?? ""),
            Telephone = PhpText.Clean(company.Phone),
            Email = PhpText.Clean(company.Email),
        };
    }

    private InvoiceParty MapCustomer(CustomerBody customer)
    {
        var tax = Get(DianCatalog.Tax, IdOr(customer.TaxId, DefaultTaxId));
        var address = PhpText.CleanOrNull(customer.Address);

        // El PHP solo escribe los bloques de dirección del cliente cuando existen la dirección y el municipio.
        var municipalityId = IdOrNull(customer.MunicipalityId);
        return new InvoiceParty
        {
            OrganizationTypeCode = Get(DianCatalog.OrganizationType, IdOr(customer.TypeOrganizationId, DefaultOrganizationTypeId)).Code,
            IdentificationNumber = PhpText.Clean(customer.IdentificationNumber!.Value.Text),
            CheckDigit = PhpText.CleanOrNull(customer.Dv),
            IdentificationTypeCode = Get(DianCatalog.IdentificationType, IdOr(customer.TypeDocumentIdentificationId, DefaultIdentificationTypeId)).Code,
            Name = PhpText.Clean(customer.Name!.Value.Text),
            Address = string.IsNullOrEmpty(address) || municipalityId is null
                ? null
                : MapAddress(municipalityId.Value, address, IdOr(customer.CountryId, DefaultCountryId), IdOr(customer.LanguageId, DefaultLanguageId)),
            RegimeCode = Get(DianCatalog.Regime, IdOr(customer.TypeRegimeId, DefaultRegimeId)).Code,
            LiabilityCode = Get(DianCatalog.Liability, IdOr(customer.TypeLiabilityId, DefaultLiabilityId)).Code,
            TaxSchemeCode = tax.Code,
            TaxSchemeName = tax.Name,
            MerchantRegistration = PhpText.CleanOrNull(customer.MerchantRegistration) ?? "",
            Telephone = PhpText.CleanOrNull(customer.Phone),
            Email = PhpText.CleanOrNull(customer.Email),
        };
    }

    private PartyAddress MapAddress(int municipalityId, string addressLine, int countryId, int languageId)
    {
        var municipality = Get(DianCatalog.Municipality, municipalityId);
        var department = municipality.Department ?? throw new InvalidOperationException($"Municipality {municipalityId} has no department.");
        var country = Get(DianCatalog.Country, countryId);
        var language = Get(DianCatalog.Language, languageId);
        return new PartyAddress(
            municipality.Code, municipality.Name, department.Name, department.Code,
            PhpText.Clean(addressLine), country.Code, country.Name, language.Code);
    }

    private PaymentTerms MapPayment(PaymentFormBody? payment)
    {
        var dueDate = payment?.PaymentDueDate is { } due && due.Text.Length > 0 ? Date(due) : (DateOnly?)null;
        var duration = payment?.DurationMeasure is { } measure && measure.Text.Length > 0 ? (int)measure.Integer : (int?)null;
        return new PaymentTerms(
            Get(DianCatalog.PaymentForm, IdOr(payment?.PaymentFormId, DefaultPaymentFormId)).Code,
            Get(DianCatalog.PaymentMethod, IdOr(payment?.PaymentMethodId, DefaultPaymentMethodId)).Code,
            dueDate,
            duration);
    }

    private static PrepaidPayment MapPrepaidPayment(PrepaidPaymentBody prepaid) => new(
        PhpText.Clean(prepaid.IdPayment!.Value.Text),
        prepaid.PaidAmount!.Value.Decimal,
        Date(prepaid.ReceivedDate!.Value),
        LegacyScalar.IsMissing(prepaid.PaidDate) ? null : Date(prepaid.PaidDate!.Value),
        prepaid.InstructionId is { Text.Length: > 0 } instruction ? PhpText.Clean(instruction.Text) : null);

    private AllowanceCharge MapAllowanceCharge(AllowanceChargeBody item)
    {
        var isCharge = item.ChargeIndicator!.Value.Boolean;
        var discountId = IdOrNull(item.DiscountId);
        return new AllowanceCharge
        {
            IsCharge = isCharge,

            // El PHP solo escribe el código de motivo de descuento para los descuentos.
            ReasonCode = isCharge || discountId is null ? null : Get(DianCatalog.Discount, discountId.Value).Code,
            Reason = PhpText.Clean(item.AllowanceChargeReason!.Value.Text),
            MultiplierFactorNumeric = item.MultiplierFactorNumeric is { } multiplier && multiplier.Text.Length > 0 ? multiplier.Decimal : null,
            Amount = item.Amount!.Value.Decimal,
            BaseAmount = Truthy(item.BaseAmount),
        };
    }

    private TaxSubtotal MapTaxSubtotal(TaxTotalBody item)
    {
        var taxId = (int)item.TaxId!.Value.Integer;
        var tax = Get(DianCatalog.Tax, taxId);
        FixedValueTax? fixedValue = null;
        if (FixedValueTaxIds.Contains(taxId) && Truthy(item.BaseUnitMeasure) is { } baseUnitMeasure)
        {
            fixedValue = new FixedValueTax(
                Get(DianCatalog.UnitMeasure, (int)item.UnitMeasureId!.Value.Integer).Code,
                baseUnitMeasure,
                item.PerUnitAmount!.Value.Decimal);
        }

        return new TaxSubtotal
        {
            SchemeCode = tax.Code,
            SchemeName = tax.Name,
            TaxableAmount = item.TaxableAmount!.Value.Decimal,
            TaxAmount = item.TaxAmount!.Value.Decimal,
            Percent = item.Percent is { } percent && percent.Text.Length > 0 ? percent.Decimal : null,
            FixedValue = fixedValue,
        };
    }

    private static MonetaryTotals MapTotals(MonetaryTotalsBody totals) => new()
    {
        LineExtensionAmount = totals.LineExtensionAmount!.Value.Decimal,
        TaxExclusiveAmount = totals.TaxExclusiveAmount!.Value.Decimal,
        TaxInclusiveAmount = totals.TaxInclusiveAmount!.Value.Decimal,
        AllowanceTotalAmount = Truthy(totals.AllowanceTotalAmount),
        ChargeTotalAmount = Truthy(totals.ChargeTotalAmount),
        PrepaidAmount = Truthy(totals.PrePaidAmount),
        PayableAmount = totals.PayableAmount!.Value.Decimal,
    };

    private InvoiceLine MapLine(LineBody line)
    {
        var itemType = Get(DianCatalog.ItemIdentificationType, (int)line.TypeItemIdentificationId!.Value.Integer);

        // La factura no escribe el tipo de precio de referencia; las notas sí, en las líneas gratuitas.
        var referencePriceId = IdOrNull(line.ReferencePriceId);
        return new InvoiceLine
        {
            UnitCode = Get(DianCatalog.UnitMeasure, (int)line.UnitMeasureId!.Value.Integer).Code,
            Quantity = line.InvoicedQuantity!.Value.Decimal,
            LineExtensionAmount = line.LineExtensionAmount!.Value.Decimal,
            IsFreeOfCharge = line.FreeOfChargeIndicator!.Value.Boolean,
            Description = PhpText.Clean(line.Description!.Value.Text),
            ItemCode = PhpText.Clean(line.Code!.Value.Text),
            ItemScheme = new ItemIdentificationScheme(itemType.Code, itemType.Name, itemType.CodeAgency ?? ""),
            PriceAmount = line.PriceAmount!.Value.Decimal,
            BaseQuantity = line.BaseQuantity!.Value.Decimal,
            ReferencePriceCode = referencePriceId is null ? null : Get(DianCatalog.ReferencePrice, referencePriceId.Value).Code,
            AllowanceCharges = MapAll(line.AllowanceCharges, MapAllowanceCharge),
            TaxSubtotals = MapAll(line.TaxTotals, MapTaxSubtotal),
        };
    }

    /// <summary>La descripción sale del catálogo de discrepancias de cada tipo de nota, no de la solicitud (como el PHP).</summary>
    private DiscrepancyResponse? MapDiscrepancy(IssueSpec spec, IssueDocumentBody body)
    {
        if (body.DiscrepancyResponseCode is not { IsPhpEmpty: false } code)
        {
            return null;
        }

        var catalog = spec.Kind == IssueKind.CreditNote ? DianCatalog.CreditNoteDiscrepancy : DianCatalog.DebitNoteDiscrepancy;
        return new DiscrepancyResponse(code.Text.Trim(), Get(catalog, (int)code.Integer).Name);
    }

    private static TaxSubtotalInput TotalsTax(TaxTotalBody item) => new()
    {
        TaxId = (int)item.TaxId!.Value.Integer,
        TaxableAmount = item.TaxableAmount!.Value.Decimal,
        TaxAmount = item.TaxAmount!.Value.Decimal,
        Percent = item.Percent is { } percent && percent.Text.Length > 0 ? percent.Decimal : null,
        PerUnitAmount = item.PerUnitAmount is { } perUnit && perUnit.Text.Length > 0 ? perUnit.Decimal : null,
        BaseUnitMeasure = item.BaseUnitMeasure is { } units && units.Text.Length > 0 ? units.Decimal : null,
    };

    private static AllowanceChargeInput TotalsCharge(AllowanceChargeBody item) =>
        new(item.ChargeIndicator!.Value.Boolean, item.Amount!.Value.Decimal);
}

/// <summary>
/// Reúne las filas de catálogo que necesita una emisión, con el campo de la solicitud de cada una, para buscarlas de
/// una vez y responder «X es inválido.» (la regla <c>exists</c> del PHP) por cada id que no exista.
/// </summary>
internal static class CatalogNeeds
{
    public static List<(CatalogKey Key, string Field)> Collect(IssueSpec spec, IssueDocumentBody body, IssuerContext issuer)
    {
        var needs = new List<(CatalogKey, string)>();
        void Need(DianCatalog catalog, int id, string field) => needs.Add((new CatalogKey(catalog, id), field));

        var company = issuer.Company;
        Need(DianCatalog.DocumentType, spec.DocumentTypeId, "type_document_id");
        Need(DianCatalog.OperationType, LegacyDocumentMapper.OperationTypeId(spec, body), "type_operation_id");
        Need(DianCatalog.Currency, company.TypeCurrencyId, "company");
        Need(DianCatalog.OrganizationType, company.TypeOrganizationId, "company");
        Need(DianCatalog.IdentificationType, company.TypeDocumentIdentificationId, "company");
        Need(DianCatalog.Regime, company.TypeRegimeId, "company");
        Need(DianCatalog.Tax, company.TaxId, "company");
        Need(DianCatalog.Municipality, company.MunicipalityId, "company");
        Need(DianCatalog.Country, company.CountryId, "company");
        Need(DianCatalog.Language, company.LanguageId, "company");
        foreach (var liability in issuer.LiabilityIds)
        {
            Need(DianCatalog.Liability, liability, "company");
        }

        if (LegacyDocumentMapper.IdOrNull(body.IdCurrency) is { } currency)
        {
            Need(DianCatalog.Currency, currency, "idcurrency");
        }

        var customer = body.Customer!;
        Need(DianCatalog.OrganizationType, LegacyDocumentMapper.IdOr(customer.TypeOrganizationId, LegacyDocumentMapper.DefaultOrganizationTypeId), "customer.type_organization_id");
        Need(DianCatalog.IdentificationType, LegacyDocumentMapper.IdOr(customer.TypeDocumentIdentificationId, LegacyDocumentMapper.DefaultIdentificationTypeId), "customer.type_document_identification_id");
        Need(DianCatalog.Regime, LegacyDocumentMapper.IdOr(customer.TypeRegimeId, LegacyDocumentMapper.DefaultRegimeId), "customer.type_regime_id");
        Need(DianCatalog.Liability, LegacyDocumentMapper.IdOr(customer.TypeLiabilityId, LegacyDocumentMapper.DefaultLiabilityId), "customer.type_liability_id");
        Need(DianCatalog.Tax, LegacyDocumentMapper.IdOr(customer.TaxId, LegacyDocumentMapper.DefaultTaxId), "customer.tax_id");
        Need(DianCatalog.Country, LegacyDocumentMapper.IdOr(customer.CountryId, LegacyDocumentMapper.DefaultCountryId), "customer.country_id");
        Need(DianCatalog.Language, LegacyDocumentMapper.IdOr(customer.LanguageId, LegacyDocumentMapper.DefaultLanguageId), "customer.language_id");
        if (LegacyDocumentMapper.IdOrNull(customer.MunicipalityId) is { } municipality)
        {
            Need(DianCatalog.Municipality, municipality, "customer.municipality_id");
        }

        foreach (var (path, payment) in LegacyDocumentMapper.PaymentFormsOf(body))
        {
            Need(DianCatalog.PaymentForm, LegacyDocumentMapper.IdOr(payment?.PaymentFormId, LegacyDocumentMapper.DefaultPaymentFormId), $"{path}.payment_form_id");
            Need(DianCatalog.PaymentMethod, LegacyDocumentMapper.IdOr(payment?.PaymentMethodId, LegacyDocumentMapper.DefaultPaymentMethodId), $"{path}.payment_method_id");
        }

        Charges(needs, "allowance_charges", body.AllowanceCharges);
        Taxes(needs, "tax_totals", body.TaxTotals);
        Taxes(needs, "with_holding_tax_total", body.WithHoldingTaxTotal);
        foreach (var (line, index) in (spec.LinesOf(body) ?? []).Select((line, index) => (line, index)))
        {
            var at = $"{spec.LinesField}.{index}";
            Need(DianCatalog.UnitMeasure, (int)line.UnitMeasureId!.Value.Integer, $"{at}.unit_measure_id");
            Need(DianCatalog.ItemIdentificationType, (int)line.TypeItemIdentificationId!.Value.Integer, $"{at}.type_item_identification_id");
            if (LegacyDocumentMapper.IdOrNull(line.ReferencePriceId) is { } referencePrice)
            {
                Need(DianCatalog.ReferencePrice, referencePrice, $"{at}.reference_price_id");
            }

            if (spec.Variant == InvoiceVariant.Transport && line.IsRndc is { } rndc && rndc.TryBoolean(out var isRndc) && isRndc)
            {
                Need(DianCatalog.UnitMeasure, (int)line.UnitMeasureConsignmentId!.Value.Integer, $"{at}.unit_measure_consignment_id");
            }

            Charges(needs, $"{at}.allowance_charges", line.AllowanceCharges);
            Taxes(needs, $"{at}.tax_totals", line.TaxTotals);
        }

        if (spec.IsNote && body.DiscrepancyResponseCode is { IsPhpEmpty: false } code)
        {
            var catalog = spec.Kind == IssueKind.CreditNote ? DianCatalog.CreditNoteDiscrepancy : DianCatalog.DebitNoteDiscrepancy;
            Need(catalog, (int)code.Integer, "discrepancyresponsecode");
        }

        return needs;
    }

    /// <summary>
    /// Un impuesto de valor fijo con unidades gravadas necesita su unidad y su valor por unidad para el XML; el PHP solo
    /// los exigía en el impuesto 10.
    /// </summary>
    public static List<FieldError> FixedValueGaps(IssueSpec spec, IssueDocumentBody body)
    {
        var errors = new List<FieldError>();
        void Check(string path, List<TaxTotalBody>? taxes)
        {
            foreach (var (tax, index) in (taxes ?? []).Select((tax, index) => (tax, index)))
            {
                if (!LegacyDocumentMapper.FixedValueTaxIds.Contains((int)tax.TaxId!.Value.Integer) || LegacyDocumentMapper.Truthy(tax.BaseUnitMeasure) is null)
                {
                    continue;
                }

                if (LegacyDocumentMapper.IdOrNull(tax.UnitMeasureId) is null)
                {
                    errors.Add(new FieldError($"{path}.{index}.unit_measure_id", IssueMessages.RequiredWith($"{path}.{index}.unit_measure_id", $"{path}.{index}.base_unit_measure")));
                }

                if (LegacyScalar.IsMissing(tax.PerUnitAmount))
                {
                    errors.Add(new FieldError($"{path}.{index}.per_unit_amount", IssueMessages.RequiredWith($"{path}.{index}.per_unit_amount", $"{path}.{index}.base_unit_measure")));
                }
            }
        }

        Check("tax_totals", body.TaxTotals);
        Check("with_holding_tax_total", body.WithHoldingTaxTotal);
        foreach (var (line, index) in (spec.LinesOf(body) ?? []).Select((line, index) => (line, index)))
        {
            Check($"{spec.LinesField}.{index}.tax_totals", line.TaxTotals);
        }

        return errors;
    }

    private static void Charges(List<(CatalogKey, string)> needs, string path, List<AllowanceChargeBody>? charges)
    {
        foreach (var (charge, index) in (charges ?? []).Select((charge, index) => (charge, index)))
        {
            if (LegacyDocumentMapper.IdOrNull(charge.DiscountId) is { } discount)
            {
                needs.Add((new CatalogKey(DianCatalog.Discount, discount), $"{path}.{index}.discount_id"));
            }
        }
    }

    private static void Taxes(List<(CatalogKey, string)> needs, string path, List<TaxTotalBody>? taxes)
    {
        foreach (var (tax, index) in (taxes ?? []).Select((tax, index) => (tax, index)))
        {
            needs.Add((new CatalogKey(DianCatalog.Tax, (int)tax.TaxId!.Value.Integer), $"{path}.{index}.tax_id"));
            if (LegacyDocumentMapper.IdOrNull(tax.UnitMeasureId) is { } unit)
            {
                needs.Add((new CatalogKey(DianCatalog.UnitMeasure, unit), $"{path}.{index}.unit_measure_id"));
            }
        }
    }
}
