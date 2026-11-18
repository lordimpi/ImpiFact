using System.Text.Json.Nodes;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Pdf;
using ImpiFact.Application.Features.Documents.Pdf;

namespace ImpiFact.Application.Tests.Features.Documents.Pdf;

/// <summary>
/// El modelo imprimible (lo que se ve en el PDF) se arma con lo guardado del documento: su detalle, la solicitud JSON
/// que lo emitió, el XML firmado, la empresa con su resolución y los catálogos.
/// </summary>
public sealed class DocumentPdfModelBuilderTests
{
    private const string WithholdingsQr =
        "https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=" +
        "0b7a6bf1ac6fcd7463acd47da12e65c7756dab27f9456b302238d082aeed99e47a82028975423120683190bcd89d3ded";

    private static readonly DocumentTotals WithholdingsTotals = new(9200000m, 9200000m, 10948000m, 0m, 0m, 0m, 10948000m);

    [Test]
    public async Task An_invoice_shows_its_number_cufe_and_the_qr_text_of_the_signed_xml()
    {
        var model = await new PdfScenario().WithGolden("invoice/withholdings-renta-iva-ica", 1, "SETP", 990000005, WithholdingsTotals).BuildAsync();

        Assert.Multiple(() =>
        {
            Assert.That(model.Kind, Is.EqualTo(PdfDocumentKind.Invoice));
            Assert.That(model.Prefix, Is.EqualTo("SETP"));
            Assert.That(model.Number, Is.EqualTo("990000005"));
            Assert.That(model.IssueDate, Is.EqualTo("2026-11-14"));
            Assert.That(model.IssueTime, Is.EqualTo("10:20:00"));
            Assert.That(model.UuidLabel, Is.EqualTo("CUFE"));
            Assert.That(model.Uuid, Is.EqualTo("cufe-990000005"));
            Assert.That(model.QrText, Is.EqualTo(WithholdingsQr));
        });
    }

    [Test]
    public async Task The_company_logo_goes_in_the_model_when_the_company_has_one()
    {
        var scenario = new PdfScenario().WithGolden("invoice/withholdings-renta-iva-ica", 1, "SETP", 990000005, WithholdingsTotals);
        var withoutLogo = await scenario.BuildAsync();
        scenario.Logos.Logos[PdfScenario.CompanyId] = new CompanyLogoImage([0x89, 0x50, 0x4E, 0x47], "image/png");

        var withLogo = await scenario.BuildAsync();

        Assert.Multiple(() =>
        {
            Assert.That(withoutLogo.Logo, Is.Null);
            Assert.That(withLogo.Logo?.ContentType, Is.EqualTo("image/png"));
            Assert.That(withLogo.Logo?.Content, Is.EqualTo(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));
        });
    }

    [Test]
    public async Task Without_a_qr_in_the_signed_xml_the_qr_is_the_environment_url_and_the_cufe()
    {
        var model = await new PdfScenario()
            .WithGolden("invoice/withholdings-renta-iva-ica", 1, "SETP", 990000005, WithholdingsTotals, withSignedXml: false)
            .BuildAsync();

        Assert.That(model.QrText, Is.EqualTo("https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=cufe-990000005"));
    }

    [Test]
    public async Task An_invoice_shows_its_taxes_withholdings_and_the_total_after_withholdings()
    {
        var model = await new PdfScenario().WithGolden("invoice/withholdings-renta-iva-ica", 1, "SETP", 990000005, WithholdingsTotals).BuildAsync();

        Assert.Multiple(() =>
        {
            Assert.That(model.Taxes, Is.EqualTo(new[] { new PdfTax("IVA", "9,200,000.00", "19.00%", "1,748,000.00") }));
            Assert.That(model.Withholdings, Is.EqualTo(new[]
            {
                new PdfTax("ReteRenta", "9,200,000.00", "4.00%", "368,000.00"),
                new PdfTax("ReteIVA", "1,748,000.00", "15.00%", "262,200.00"),
                new PdfTax("ReteICA", "9,200,000.00", "0.966%", "88,872.00"),
            }));

            // Como el PHP: el total a pagar es el valor a pagar menos las retenciones (10.948.000 - 719.072).
            Assert.That(model.Totals, Is.EqualTo(new PdfTotals("2", "9,200,000.00", "1,748,000.00", "719,072.00", "0.00", "10,228,928.00")));
            Assert.That(model.AmountInWords, Is.EqualTo("DIEZ MILLONES DOSCIENTOS VEINTIOCHO MIL NOVECIENTOS VEINTIOCHO PESOS CON CERO CENTAVOS"));
        });
    }

    [Test]
    public async Task An_invoice_shows_the_issuer_its_resolution_and_the_customer()
    {
        var model = await new PdfScenario().WithGolden("invoice/withholdings-renta-iva-ica", 1, "SETP", 990000005, WithholdingsTotals).BuildAsync();

        Assert.Multiple(() =>
        {
            Assert.That(model.Issuer, Is.EqualTo(new PdfIssuer(
                "EMPRESA EMISORA SAS", "900123456-7", "Responsable de IVA",
                "Agente de retención en el impuesto sobre las ventas, No aplica – Otros",
                "CALLE 1 # 2-03", "Bogotá, D.c. - Bogotá - Colombia", "6015550000", "facturacion@emisora.example")));
            Assert.That(model.Resolution, Is.EqualTo(new PdfResolution(
                "18760000001", "2019-01-19", "SETP", "990000000", "995000000", "2019-01-19", "2030-01-19")));
            Assert.That(model.Customer, Is.EqualTo(new PdfCustomer(
                "800000001-1", "CLIENTE DE PRUEBA GOLDEN SAS", "Responsable de IVA", "No aplica – Otros", "CARRERA 2 # 2-02",
                "Bogotá, D.c. - Colombia", "3000000001", "cliente.golden@example.com")));
            Assert.That(model.Payments, Is.EqualTo(new[] { new PdfPayment("Contado", "Efectivo", "0", "2026-11-14") }));
            Assert.That(model.SoftwareMode, Does.Contain("Software propio"));
        });
    }

    [Test]
    public async Task Lines_show_the_unit_price_tax_and_discount_per_unit()
    {
        var totals = new DocumentTotals(1327750m, 1327750m, 1580022.50m, 0m, 35000m, 0m, 1615022.50m);

        var model = await new PdfScenario().WithGolden("invoice/global-charge-credit-payment", 1, "SETP", 990000008, totals).BuildAsync();

        Assert.Multiple(() =>
        {
            Assert.That(model.Lines, Is.EqualTo(new[]
            {
                // 50 unidades a 18.900 con descuento de 47.250 (5 % de 945.000): 945 de descuento y 3.411,45 de IVA por unidad.
                new PdfLine(1, "MAT01", "Material de construcción", null, "50.00", "Unidad", "18,900.00", "3,411.45", "945.00", "5.00", "897,750.00"),

                // Un cargo de la línea no es un descuento.
                new PdfLine(2, "HER01", "Herramienta", null, "2.00", "Unidad", "210,000.00", "40,850.00", "0.00", "0.00", "430,000.00"),
            }));
            Assert.That(model.Charges, Is.EqualTo(new[] { new PdfCharge("Flete", "35,000.00") }));
            Assert.That(model.Payments, Is.EqualTo(new[] { new PdfPayment("Crédito", "Transferecia Crédito", "30", "2026-12-14") }));
        });
    }

    [Test]
    public async Task A_line_without_taxes_is_marked_as_excluded()
    {
        var model = await new PdfScenario().WithGolden(
            "invoice/single-line-iva19", 1, "SETP", 990000002, new DocumentTotals(1000m, 0m, 1000m, 0m, 0m, 0m, 1000m),
            editRequest: request =>
            {
                var root = JsonNode.Parse(request)!.AsObject();
                foreach (var line in root["invoice_lines"]!.AsArray())
                {
                    line!.AsObject().Remove("tax_totals");
                }

                return root.ToJsonString();
            }).BuildAsync();

        Assert.That(model.Lines.Select(line => line.UnitTax), Is.All.EqualTo("E"));
    }

    [Test]
    public async Task User_text_is_kept_as_is_for_the_template_to_escape()
    {
        var model = await new PdfScenario().WithGolden(
            "invoice/withholdings-renta-iva-ica", 1, "SETP", 990000005, WithholdingsTotals, notes: "<b>nota</b>",
            editRequest: request => request.Replace("Desarrollo de software", "<script>alert(1)</script>", StringComparison.Ordinal)).BuildAsync();

        Assert.Multiple(() =>
        {
            Assert.That(model.Lines[0].Description, Is.EqualTo("<script>alert(1)</script>"));
            Assert.That(model.Notes, Is.EqualTo("<b>nota</b>"));
        });
    }

    [Test]
    public async Task The_validation_date_is_when_the_dian_accepted_it_in_colombian_time()
    {
        var model = await new PdfScenario().WithGolden(
            "invoice/withholdings-renta-iva-ica", 1, "SETP", 990000005, WithholdingsTotals,
            acceptedAt: new DateTimeOffset(2026, 11, 15, 2, 5, 9, TimeSpan.Zero)).BuildAsync();

        Assert.Multiple(() =>
        {
            Assert.That(model.ValidationDate, Is.EqualTo("2026-11-14"));
            Assert.That(model.ValidationTime, Is.EqualTo("21:05:09"));
        });
    }

    [Test]
    public async Task A_credit_note_shows_its_cude_and_the_invoice_it_refers_to_without_a_resolution()
    {
        var totals = new DocumentTotals(950m, 950m, 1130.50m, 0m, 0m, 0m, 1130.50m);

        var model = await new PdfScenario().WithGolden("credit-note/partial-return-with-reference", 4, "NC", 1, totals).BuildAsync();

        Assert.Multiple(() =>
        {
            Assert.That(model.Kind, Is.EqualTo(PdfDocumentKind.CreditNote));
            Assert.That(model.UuidLabel, Is.EqualTo("CUDE"));
            Assert.That(model.Resolution, Is.Null);
            Assert.That(model.Reference, Is.EqualTo(new PdfReference(
                "SETP990000001", "2026-11-14",
                "d504983c0da24a41b26b37dbd04901ce7dfa6f8f395395c18d283bf219a051e441456ea841366e59f3bfe8f91b82c122")));
            Assert.That(model.Lines.Single().Description, Is.EqualTo("Producto de prueba 1"));

            // En las notas el total es el de la nota, sin restar retenciones.
            Assert.That(model.Totals.Payable, Is.EqualTo("1,130.50"));
            Assert.That(model.AmountInWords, Is.EqualTo("MIL CIENTO TREINTA PESOS CON CINCUENTA CENTAVOS"));
        });
    }

    [Test]
    public async Task A_debit_note_uses_its_lines_and_requested_totals()
    {
        var totals = new DocumentTotals(100m, 100m, 119m, 0m, 0m, 0m, 119m);

        var model = await new PdfScenario().WithGolden("debit-note/price-adjustment-with-reference", 5, "ND", 1, totals).BuildAsync();

        Assert.Multiple(() =>
        {
            Assert.That(model.Kind, Is.EqualTo(PdfDocumentKind.DebitNote));
            Assert.That(model.UuidLabel, Is.EqualTo("CUDE"));
            Assert.That(model.Lines.Single(), Is.EqualTo(new PdfLine(1, "AJU01", "Ajuste de precio", null, "1.00", "Unidad", "100.00", "19.00", "0.00", "0.00", "100.00")));
            Assert.That(model.Totals, Is.EqualTo(new PdfTotals("1", "100.00", "19.00", "0.00", "0.00", "119.00")));
            Assert.That(model.Reference?.Number, Is.EqualTo("SETP990000001"));
        });
    }

    [Test]
    public async Task A_missing_document_gives_no_model()
    {
        var model = await new PdfScenario().Builder().BuildAsync(PdfScenario.CompanyId, Guid.CreateVersion7(), CancellationToken.None);

        Assert.That(model, Is.Null);
    }

    [Test]
    public void A_document_without_its_stored_request_cannot_be_printed()
    {
        var scenario = new PdfScenario().WithGolden("invoice/withholdings-renta-iva-ica", 1, "SETP", 990000005, WithholdingsTotals);
        scenario.Db.Files.RemoveAll(file => file.Kind == Domain.Documents.DocumentFileKind.RequestJson);

        Assert.ThrowsAsync<PdfSourceException>(scenario.BuildAsync);
    }
}
