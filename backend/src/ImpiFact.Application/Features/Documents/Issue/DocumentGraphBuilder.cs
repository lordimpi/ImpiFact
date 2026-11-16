using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Dian.Rounding;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Features.Documents.Issue;

/// <summary>
/// Arma las filas del documento emitido (Modelo de datos detallado, sección 3.5) con los valores del cliente: la
/// cabecera con la copia congelada del adquiriente, las líneas, los impuestos, los descuentos y cargos, el pago y la
/// referencia a la factura de una nota. El documento nace en <see cref="DocumentStatus.Received"/>; el caso de uso lo
/// pasa a <see cref="DocumentStatus.Queued"/> con <see cref="Document.MoveTo"/> cuando ya guardó el XML firmado.
/// </summary>
internal static class DocumentGraphBuilder
{
    public static DocumentGraph Build(
        IssueFrame frame,
        IssueDocumentBody body,
        string uuid,
        Guid? referenceDocumentId,
        ITenantContext tenant)
    {
        var spec = frame.Spec;
        var companyId = frame.Issuer.Company.Id;
        var documentId = Guid.CreateVersion7();
        var totals = spec.TotalsOf(body)!;
        var prefix = frame.Resolution.Prefix ?? "";
        var customer = body.Customer!;

        var document = new Document
        {
            Id = documentId,
            CompanyId = companyId,
            DocumentTypeId = spec.DocumentTypeId,
            TypeEnvironmentId = frame.Issuer.TypeEnvironmentId,
            ResolutionId = frame.Resolution.Id,
            Prefix = prefix,
            Number = frame.Number,
            FullNumber = prefix + frame.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Cufe = uuid,
            UuidAlgorithm = spec.UuidAlgorithm,
            IssueDate = frame.IssueDate,
            IssueTime = frame.IssueTime,
            IssuedAt = ColombiaTime.ToUtc(frame.IssueDate, frame.IssueTime),
            CustomerSnapshot = new CustomerSnapshot
            {
                TypeDocumentIdentificationId = LegacyDocumentMapper.IdOr(customer.TypeDocumentIdentificationId, LegacyDocumentMapper.DefaultIdentificationTypeId),
                IdentificationNumber = customer.IdentificationNumber!.Value.Text,
                Dv = customer.Dv?.Text,
                Name = customer.Name!.Value.Text,
                Phone = customer.Phone?.Text,
                Address = customer.Address?.Text,
                Email = customer.Email?.Text,
                MunicipalityId = LegacyDocumentMapper.IdOrNull(customer.MunicipalityId),
                TypeOrganizationId = LegacyDocumentMapper.IdOr(customer.TypeOrganizationId, LegacyDocumentMapper.DefaultOrganizationTypeId),
                TypeRegimeId = LegacyDocumentMapper.IdOr(customer.TypeRegimeId, LegacyDocumentMapper.DefaultRegimeId),
                TypeLiabilityId = LegacyDocumentMapper.IdOr(customer.TypeLiabilityId, LegacyDocumentMapper.DefaultLiabilityId),
                MerchantRegistration = customer.MerchantRegistration?.Text,
            },
            CurrencyId = LegacyDocumentMapper.IdOrNull(body.IdCurrency) ?? frame.Issuer.Company.TypeCurrencyId,
            ExchangeRate = body.CalculationRate is { } rate && rate.Text.Length > 0 ? DianRounding.RoundQuantity(rate.Decimal) : null,
            TypeOperationId = LegacyDocumentMapper.OperationTypeId(spec, body),
            LineExtensionAmount = Amount(totals.LineExtensionAmount),
            TaxExclusiveAmount = Amount(totals.TaxExclusiveAmount),
            TaxInclusiveAmount = Amount(totals.TaxInclusiveAmount),
            AllowanceTotalAmount = Amount(totals.AllowanceTotalAmount),
            ChargeTotalAmount = Amount(totals.ChargeTotalAmount),
            PrepaidAmount = Amount(totals.PrePaidAmount),
            PayableAmount = Amount(totals.PayableAmount),
            ReferenceDocumentId = referenceDocumentId,
            DiscrepancyCode = spec.IsNote ? LegacyDocumentMapper.IdOrNull(body.DiscrepancyResponseCode) : null,
            DiscrepancyDescription = spec.IsNote ? body.DiscrepancyResponseDescription?.Text : null,
            Notes = body.Notes?.Text,
            HeadNote = body.HeadNote?.Text,
            FootNote = body.FootNote?.Text,
            CreatedByType = (ActorKind)tenant.Actor,
            CreatedById = tenant.UserId,
        };

        var lines = new List<DocumentLine>();
        var lineTaxes = new List<DocumentLineTax>();
        var charges = Charges(companyId, documentId, null, body.AllowanceCharges);
        foreach (var (line, index) in (spec.LinesOf(body) ?? []).Select((line, index) => (line, index)))
        {
            var lineId = Guid.CreateVersion7();
            lines.Add(new DocumentLine
            {
                Id = lineId,
                CompanyId = companyId,
                DocumentId = documentId,
                LineNumber = index + 1,
                Code = line.Code?.Text,
                Description = line.Description!.Value.Text,
                Notes = line.Notes?.Text,
                TypeItemIdentificationId = (int)line.TypeItemIdentificationId!.Value.Integer,
                UnitMeasureId = (int)line.UnitMeasureId!.Value.Integer,
                Quantity = Quantity(line.InvoicedQuantity),
                BaseQuantity = Quantity(line.BaseQuantity),
                UnitPrice = Quantity(line.PriceAmount),
                LineExtensionAmount = Amount(line.LineExtensionAmount),
                FreeOfCharge = line.FreeOfChargeIndicator!.Value.Boolean,
                ReferencePriceId = LegacyDocumentMapper.IdOrNull(line.ReferencePriceId),
            });
            lineTaxes.AddRange((line.TaxTotals ?? []).Select(tax => new DocumentLineTax
            {
                CompanyId = companyId,
                DocumentId = documentId,
                LineId = lineId,
                TaxId = (int)tax.TaxId!.Value.Integer,
                Percent = Percent(tax.Percent),
                TaxableAmount = Amount(tax.TaxableAmount),
                TaxAmount = Amount(tax.TaxAmount),
                PerUnitAmount = tax.PerUnitAmount is { } perUnit && perUnit.Text.Length > 0 ? Quantity(perUnit) : null,
                UnitMeasureId = LegacyDocumentMapper.IdOrNull(tax.UnitMeasureId),
            }));
            charges.AddRange(Charges(companyId, documentId, lineId, line.AllowanceCharges));
        }

        // Las retenciones van con los impuestos: su tax_id (05, 06, 07…) las distingue.
        var taxes = (body.TaxTotals ?? []).Concat(body.WithHoldingTaxTotal ?? []).Select(tax => new DocumentTax
        {
            CompanyId = companyId,
            DocumentId = documentId,
            TaxId = (int)tax.TaxId!.Value.Integer,
            Percent = Percent(tax.Percent),
            TaxableAmount = Amount(tax.TaxableAmount),
            TaxAmount = Amount(tax.TaxAmount),
        }).ToList();

        var payment = body.PaymentForm is { Count: > 0 } forms ? forms[0] : null;
        var payments = new List<DocumentPayment>
        {
            new()
            {
                CompanyId = companyId,
                DocumentId = documentId,
                PaymentFormId = LegacyDocumentMapper.IdOr(payment?.PaymentFormId, LegacyDocumentMapper.DefaultPaymentFormId),
                PaymentMethodId = LegacyDocumentMapper.IdOr(payment?.PaymentMethodId, LegacyDocumentMapper.DefaultPaymentMethodId),
                DueDate = payment?.PaymentDueDate is { } due && due.Text.Length > 0 ? LegacyDocumentMapper.Date(due) : null,
                DurationMeasure = payment?.DurationMeasure?.Text,
            },
        };

        var references = new List<DocumentReference>();
        if (spec.IsNote && body.BillingReference is { Number: { } number, Uuid: { } referenced, IssueDate: { } issued })
        {
            references.Add(new DocumentReference
            {
                CompanyId = companyId,
                DocumentId = documentId,
                Kind = DocumentReferenceKind.BillingReference,
                Number = number.Text,
                Uuid = referenced.Text,
                IssueDate = LegacyDocumentMapper.Date(issued),
            });
        }

        return new DocumentGraph(document, lines, taxes, lineTaxes, charges, payments, references);
    }

    private static List<DocumentAllowanceCharge> Charges(Guid companyId, Guid documentId, Guid? lineId, List<AllowanceChargeBody>? items) =>
        (items ?? []).Select(item => new DocumentAllowanceCharge
        {
            CompanyId = companyId,
            DocumentId = documentId,
            LineId = lineId,
            ChargeIndicator = item.ChargeIndicator!.Value.Boolean,
            DiscountId = LegacyDocumentMapper.IdOrNull(item.DiscountId),
            Reason = item.AllowanceChargeReason?.Text,
            Amount = Amount(item.Amount),
            BaseAmount = Amount(item.BaseAmount),
            MultiplierFactor = Percent(item.MultiplierFactorNumeric),
        }).ToList();

    /// <summary>Importe con 2 decimales, como queda en el XML; vacío cuenta como 0.</summary>
    private static decimal Amount(LegacyScalar? value) =>
        value is { } scalar && scalar.TryDecimal(out var amount) ? DianRounding.RoundAmount(amount) : 0m;

    private static decimal Quantity(LegacyScalar? value) =>
        value is { } scalar && scalar.TryDecimal(out var quantity) ? DianRounding.RoundQuantity(quantity) : 0m;

    /// <summary>Porcentaje con los 4 decimales de su columna.</summary>
    private static decimal? Percent(LegacyScalar? value) =>
        value is { } scalar && scalar.TryDecimal(out var percent) ? Math.Round(percent, 4, MidpointRounding.AwayFromZero) : null;
}
