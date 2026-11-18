using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Pdf;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Features.Documents.Pdf;

/// <summary>
/// Arma el modelo imprimible (<see cref="DocumentPdfModel"/>) de un documento ya emitido, con lo mismo que usan las
/// plantillas del PHP (ApiDian, la implementación de referencia): la cabecera, el adquiriente y los totales del
/// documento guardado; las líneas, impuestos, retenciones, pagos, cargos y la factura de referencia de la solicitud JSON
/// guardada (<c>document_files</c> de tipo solicitud, la misma que el PHP le pasa a la plantilla como <c>$request</c>);
/// la empresa y su resolución de <see cref="IIssuerContextReads"/>, el logo de la empresa de <see cref="ICompanyLogos"/>
/// y los nombres de los catálogos DIAN.
/// </summary>
/// <remarks>
/// El texto del código QR se lee del <c>sts:QRCode</c> del XML firmado, así es exactamente el que recibió la DIAN
/// aunque la configuración de URLs haya cambiado después; si el XML no lo trae, se arma igual que al emitir: la URL de
/// consulta del ambiente (<see cref="IDianQrUrls"/>) seguida del CUFE o CUDE.
/// </remarks>
internal sealed class DocumentPdfModelBuilder(
    IDocumentReads reads,
    IDocumentStore store,
    IIssuerContextReads issuers,
    IDocumentCatalogs catalogs,
    IDianQrUrls qrUrls,
    ICompanyLogos logos)
{
    /// <summary>País del adquiriente cuando la solicitud no lo trae (Colombia, como el PHP).</summary>
    private const int Colombia = 46;

    /// <summary>Software con que se generó el documento, para el pie de página.</summary>
    private const string SoftwareMode = "Software propio - ImpiFact";

    /// <summary>Modelo del documento; nulo si el documento no existe en la empresa.</summary>
    /// <exception cref="PdfSourceException">Si falta la solicitud guardada o el tipo de documento no tiene plantilla.</exception>
    public async Task<DocumentPdfModel?> BuildAsync(Guid companyId, Guid documentId, CancellationToken ct)
    {
        var detail = await reads.FindByIdAsync(companyId, documentId, ct);
        if (detail is null)
        {
            return null;
        }

        var header = detail.Header;
        var kind = KindOf(header.DocumentTypeId)
            ?? throw new PdfSourceException($"Document {documentId} has type {header.DocumentTypeId}, which has no PDF template yet.");
        var requestBytes = await store.ReadFileAsync(companyId, documentId, DocumentFileKind.RequestJson, ct)
            ?? throw new PdfSourceException($"Document {documentId} has no stored request.");
        if (!IssueDocumentJson.TryParse(requestBytes, out var parsed) || parsed is null)
        {
            throw new PdfSourceException($"The stored request of document {documentId} is not valid JSON.");
        }

        var body = parsed;
        var issuer = await issuers.GetAsync(companyId, header.DocumentTypeId, DocumentKind.Invoice, ct)
            ?? throw new PdfSourceException($"Company {companyId} does not exist.");
        var lines = (kind switch
        {
            PdfDocumentKind.CreditNote => body.CreditNoteLines,
            PdfDocumentKind.DebitNote => body.DebitNoteLines,
            _ => body.InvoiceLines,
        }) ?? [];
        var names = new Names(await catalogs.FindAsync(CatalogKeys(issuer, header.Customer, body, lines), ct));

        var taxes = (body.TaxTotals ?? []).Select(tax => Tax(tax, names)).ToList();
        var withholdings = (body.WithHoldingTaxTotal ?? []).Select(tax => Tax(tax, names)).ToList();
        var taxTotal = (body.TaxTotals ?? []).Sum(tax => Amount(tax.TaxAmount));
        var withholdingTotal = (body.WithHoldingTaxTotal ?? []).Sum(tax => Amount(tax.TaxAmount));

        // Como el PHP: en la factura se paga el valor a pagar menos las retenciones; en las notas, el total de la nota.
        var payable = kind == PdfDocumentKind.Invoice ? header.Totals.PayableAmount - withholdingTotal : header.Totals.PayableAmount;
        var currencyName = body.IdCurrency is { IsPhpEmpty: false } currency && currency.TryInteger(out var currencyId)
            ? names.Of(DianCatalog.Currency, (int)currencyId)
            : null;
        var (validationDate, validationTime) = detail.AcceptedAt is { } acceptedAt
            ? (ColombiaTime.Format(DateOnly.FromDateTime(ColombiaTime.Local(acceptedAt).DateTime)), Time(TimeOnly.FromDateTime(ColombiaTime.Local(acceptedAt).DateTime)))
            : (ColombiaTime.Format(header.IssueDate), Time(header.IssueTime));
        var uuid = header.Cufe ?? "";

        return new DocumentPdfModel(
            kind,
            header.Prefix,
            header.Number.ToString(CultureInfo.InvariantCulture),
            ColombiaTime.Format(header.IssueDate),
            Time(header.IssueTime),
            validationDate,
            validationTime,
            kind == PdfDocumentKind.Invoice ? "CUFE" : "CUDE",
            uuid,
            await QrTextAsync(companyId, documentId, header.TypeEnvironmentId, uuid, ct),
            Issuer(issuer, names),
            kind == PdfDocumentKind.Invoice ? Resolution(issuer, detail.ResolutionId) : null,
            Customer(header.Customer, body.Customer, names),
            (body.PaymentForm ?? []).Select(payment => Payment(payment, names)).ToList(),
            body.BillingReference is { } reference
                ? new PdfReference(Text(reference.Number), Text(reference.IssueDate), Text(reference.Uuid))
                : null,
            body.InvoicePeriod is { } period ? $"{Text(period.StartDate)} - {Text(period.EndDate)}" : null,
            lines.Select((line, index) => Line(index + 1, line, names)).ToList(),
            taxes,
            withholdings,
            Charges(body.AllowanceCharges),
            new PdfTotals(
                lines.Count.ToString(CultureInfo.InvariantCulture),
                Money(header.Totals.LineExtensionAmount),
                Money(taxTotal),
                Money(withholdingTotal),
                Money(header.Totals.AllowanceTotalAmount),
                Money(payable)),
            AmountInWords.Spell(Round(payable), currencyName),
            detail.Notes,
            detail.HeadNote,
            detail.FootNote,
            SoftwareMode,
            await LogoAsync(logos, companyId, ct));
    }

    /// <summary>Logo de la empresa para el encabezado del PDF; nulo si no tiene.</summary>
    internal static async Task<PdfImage?> LogoAsync(ICompanyLogos logos, Guid companyId, CancellationToken ct) =>
        await logos.FindAsync(companyId, ct) is { } logo ? new PdfImage(logo.Content, logo.ContentType) : null;

    private static PdfDocumentKind? KindOf(int documentTypeId) => documentTypeId switch
    {
        // Factura de venta nacional, de exportación y de contingencia.
        1 or 2 or 3 => PdfDocumentKind.Invoice,
        4 => PdfDocumentKind.CreditNote,
        5 => PdfDocumentKind.DebitNote,
        _ => null,
    };

    private async Task<string> QrTextAsync(Guid companyId, Guid documentId, int typeEnvironmentId, string uuid, CancellationToken ct)
    {
        var signedXml = await store.ReadFileAsync(companyId, documentId, DocumentFileKind.SignedXml, ct);
        return QrCodeOf(signedXml) ?? qrUrls.ForEnvironment(typeEnvironmentId) + uuid;
    }

    /// <summary>Texto del <c>sts:QRCode</c> del XML firmado; nulo si no hay XML o no lo trae.</summary>
    private static string? QrCodeOf(byte[]? signedXml)
    {
        if (signedXml is null)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(signedXml);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var code = XDocument.Load(reader).Descendants().FirstOrDefault(element => element.Name.LocalName == "QRCode")?.Value.Trim();
            return string.IsNullOrEmpty(code) ? null : code;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static PdfIssuer Issuer(IssuerContext issuer, Names names)
    {
        var company = issuer.Company;
        var municipality = names.Item(DianCatalog.Municipality, company.MunicipalityId);
        var location = Join(" - ", municipality?.Name, municipality?.Department?.Name, names.Of(DianCatalog.Country, company.CountryId));
        return new PdfIssuer(
            company.Name,
            Join("-", company.IdentificationNumber, company.Dv),
            names.Of(DianCatalog.Regime, company.TypeRegimeId),
            string.Join(", ", issuer.LiabilityIds.Select(id => names.Of(DianCatalog.Liability, id)).Where(name => name.Length > 0)),
            company.Address,
            location,
            company.Phone,
            company.Email);
    }

    private static PdfResolution? Resolution(IssuerContext issuer, Guid? resolutionId) =>
        issuer.Resolutions.FirstOrDefault(resolution => resolution.Id == resolutionId) is { } resolution
            ? new PdfResolution(
                resolution.ResolutionNumber ?? "",
                Date(resolution.ResolutionDate),
                resolution.Prefix ?? "",
                resolution.From.ToString(CultureInfo.InvariantCulture),
                resolution.To.ToString(CultureInfo.InvariantCulture),
                Date(resolution.ValidFrom),
                Date(resolution.ValidTo))
            : null;

    private static PdfCustomer Customer(DocumentCustomer customer, CustomerBody? requested, Names names)
    {
        var countryId = LegacyDocumentMapper.IdOr(requested?.CountryId, Colombia);
        var municipality = customer.MunicipalityId is { } municipalityId ? names.Item(DianCatalog.Municipality, municipalityId)?.Name : null;
        return new PdfCustomer(
            Join("-", customer.IdentificationNumber, customer.Dv),
            customer.Name,
            customer.TypeRegimeId is { } regime ? names.Of(DianCatalog.Regime, regime) : "",
            customer.TypeLiabilityId is { } liability ? names.Of(DianCatalog.Liability, liability) : "",
            customer.Address ?? "",
            Join(" - ", municipality, names.Of(DianCatalog.Country, countryId)),
            customer.Phone ?? "",
            customer.Email ?? "");
    }

    private static PdfPayment Payment(PaymentFormBody payment, Names names) => new(
        Id(payment.PaymentFormId) is { } form ? names.Of(DianCatalog.PaymentForm, form) : "",
        Id(payment.PaymentMethodId) is { } method ? names.Of(DianCatalog.PaymentMethod, method) : "",
        Text(payment.DurationMeasure),
        Text(payment.PaymentDueDate));

    /// <summary>
    /// Línea con las columnas de la plantilla del PHP: valor unitario (el precio antes del descuento), impuesto y
    /// descuento por unidad, porcentaje del descuento y valor de la línea (su base, sin impuestos). Una línea sin
    /// impuestos muestra «E» (excluida). Los cargos de la línea no cuentan como descuento.
    /// </summary>
    private static PdfLine Line(int position, LineBody line, Names names)
    {
        var quantity = Amount(line.InvoicedQuantity);
        var discounts = (line.AllowanceCharges ?? []).Where(charge => !IsCharge(charge)).ToList();
        var discount = discounts.Sum(charge => Amount(charge.Amount));
        var discountBase = discounts.Count > 0 ? Amount(discounts[0].BaseAmount) : 0m;
        var unitTax = line.TaxTotals is { Count: > 0 } lineTaxes ? Money(PerUnit(lineTaxes.Sum(tax => Amount(tax.TaxAmount)), quantity)) : "E";
        return new PdfLine(
            position,
            Text(line.Code),
            Text(line.Description),
            line.Notes is { IsPhpEmpty: false } notes ? notes.Text : null,
            Money(quantity),
            Id(line.UnitMeasureId) is { } unit ? names.Of(DianCatalog.UnitMeasure, unit) : "",
            Money(Amount(line.PriceAmount)),
            unitTax,
            Money(PerUnit(discount, quantity)),
            Money(discountBase == 0m ? 0m : discount * 100m / discountBase),
            Money(Amount(line.LineExtensionAmount)));
    }

    private static PdfTax Tax(TaxTotalBody tax, Names names)
    {
        var rate = tax.Percent is { } percent && percent.TryDecimal(out var value) ? Rate(value) + "%"
            : tax.PerUnitAmount is { } perUnit && tax.BaseUnitMeasure is { } baseUnit
                ? $"{Money(Amount(perUnit))} x {Money(Amount(baseUnit))}"
                : "-";
        return new PdfTax(
            Id(tax.TaxId) is { } taxId ? names.Of(DianCatalog.Tax, taxId) : "",
            Money(Amount(tax.TaxableAmount)),
            rate,
            Money(Amount(tax.TaxAmount)));
    }

    /// <summary>Cargos de la cabecera, con su motivo o «Cargo Global Nro: n» como el PHP.</summary>
    private static List<PdfCharge> Charges(List<AllowanceChargeBody>? allowanceCharges)
    {
        var charges = (allowanceCharges ?? []).Where(IsCharge).ToList();
        return charges.Select((charge, index) => new PdfCharge(
            charge.AllowanceChargeReason is { IsPhpEmpty: false } reason ? reason.Text : $"Cargo Global Nro: {index + 1}",
            Money(Amount(charge.Amount)))).ToList();
    }

    private static List<CatalogKey> CatalogKeys(IssuerContext issuer, DocumentCustomer customer, IssueDocumentBody body, List<LineBody> lines)
    {
        var company = issuer.Company;
        var keys = new List<CatalogKey>
        {
            new(DianCatalog.Municipality, company.MunicipalityId),
            new(DianCatalog.Country, company.CountryId),
            new(DianCatalog.Regime, company.TypeRegimeId),
            new(DianCatalog.Country, LegacyDocumentMapper.IdOr(body.Customer?.CountryId, Colombia)),
        };
        keys.AddRange(issuer.LiabilityIds.Select(id => new CatalogKey(DianCatalog.Liability, id)));
        Add(keys, DianCatalog.Municipality, customer.MunicipalityId);
        Add(keys, DianCatalog.Regime, customer.TypeRegimeId);
        Add(keys, DianCatalog.Liability, customer.TypeLiabilityId);
        Add(keys, DianCatalog.Currency, Id(body.IdCurrency));
        foreach (var payment in body.PaymentForm ?? [])
        {
            Add(keys, DianCatalog.PaymentForm, Id(payment.PaymentFormId));
            Add(keys, DianCatalog.PaymentMethod, Id(payment.PaymentMethodId));
        }

        foreach (var tax in (body.TaxTotals ?? []).Concat(body.WithHoldingTaxTotal ?? []))
        {
            Add(keys, DianCatalog.Tax, Id(tax.TaxId));
        }

        foreach (var line in lines)
        {
            Add(keys, DianCatalog.UnitMeasure, Id(line.UnitMeasureId));
        }

        return keys.Distinct().ToList();
    }

    private static void Add(List<CatalogKey> keys, DianCatalog catalog, int? id)
    {
        if (id is { } value)
        {
            keys.Add(new CatalogKey(catalog, value));
        }
    }

    private static bool IsCharge(AllowanceChargeBody charge) => charge.ChargeIndicator is { } value && value.TryBoolean(out var isCharge) && isCharge;

    private static int? Id(LegacyScalar? value) => value is { IsPhpEmpty: false } scalar && scalar.TryInteger(out var id) ? (int)id : null;

    private static decimal Amount(LegacyScalar? value) => value is { } scalar && scalar.TryDecimal(out var amount) ? amount : 0m;

    private static string Text(LegacyScalar? value) => value?.Text ?? "";

    private static decimal PerUnit(decimal amount, decimal quantity) => quantity == 0m ? 0m : amount / quantity;

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Importe con dos decimales, coma de miles y punto decimal, como <c>number_format($x, 2)</c> del PHP.</summary>
    private static string Money(decimal value) => Round(value).ToString("#,##0.00", CultureInfo.InvariantCulture);

    /// <summary>Porcentaje con al menos dos decimales y sin perder los que trae (0,966 % de ReteICA queda «0.966»).</summary>
    private static string Rate(decimal value) => value.ToString("0.00##", CultureInfo.InvariantCulture);

    private static string Time(TimeOnly time) => time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    private static string Date(DateOnly? date) => date is { } value ? ColombiaTime.Format(value) : "";

    private static string Join(string separator, params string?[] parts) =>
        string.Join(separator, parts.Select(part => part?.Trim()).Where(part => !string.IsNullOrEmpty(part)));

    /// <summary>Nombres de los catálogos pedidos; vacío si una fila no existe (la plantilla no se rompe por un id raro).</summary>
    private sealed class Names(IReadOnlyDictionary<CatalogKey, CatalogItem> items)
    {
        public CatalogItem? Item(DianCatalog catalog, int id) => items.GetValueOrDefault(new CatalogKey(catalog, id));

        public string Of(DianCatalog catalog, int id) => Item(catalog, id)?.Name.Trim() ?? "";
    }
}
