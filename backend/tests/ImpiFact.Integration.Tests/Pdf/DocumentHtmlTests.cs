using ImpiFact.Application.Abstractions.Pdf;
using ImpiFact.Infrastructure.Pdf;

namespace ImpiFact.Integration.Tests.Pdf;

/// <summary>
/// Las plantillas HTML del PDF (Handlebars, con escape HTML activo) con un modelo de ejemplo. Corren sin Docker: solo
/// arman el HTML que después se convierte en PDF.
/// </summary>
public sealed class DocumentHtmlTests
{
    private readonly DocumentHtmlTemplates templates = new();

    [Test]
    public void User_text_is_html_escaped()
    {
        var html = templates.Render(PdfSamples.Invoice("<script>alert('x')</script>", notes: "<img src=x onerror=alert(1)>"));

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Not.Contain("<script>"));
            Assert.That(html, Does.Not.Contain("<img src=x"));
            Assert.That(html, Does.Contain("&lt;script&gt;"));
            Assert.That(html, Does.Contain("&lt;img src"));
        });
    }

    [Test]
    public void An_invoice_shows_its_cufe_qr_issuer_resolution_and_customer()
    {
        var html = templates.Render(PdfSamples.Invoice());

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("FACTURA ELECTRONICA DE VENTA"));
            Assert.That(html, Does.Contain("REPRESENTACION GRAFICA DE FACTURA ELECTRONICA"));
            Assert.That(html, Does.Contain("SETP - 990000005"));
            Assert.That(html, Does.Contain("CUFE: <span class=\"uuid\">" + PdfSamples.Cufe + "</span>"));
            Assert.That(html, Does.Contain("src=\"qr.png\""));
            Assert.That(html, Does.Contain("NIT: 900123456-7 - Responsable de IVA - Obligación: No aplica – Otros"));
            Assert.That(html, Does.Contain(
                "Resolución de Facturación Electrónica No. 18760000001 de 2019-01-19, Prefijo: SETP, Rango 990000000 Al 995000000 - Vigencia Desde: 2019-01-19 Hasta: 2030-01-19"));
            Assert.That(html, Does.Contain("CLIENTE DE PRUEBA GOLDEN SAS"));
            Assert.That(html, Does.Contain("Plazo Para Pagar:</td><td>0 Días"));
            Assert.That(html, Does.Contain("Modo de operación: Software propio - ImpiFact"));
            Assert.That(html, Does.Contain("título valor"));
            Assert.That(html, Does.Contain("Pie de página de la empresa"));
            Assert.That(html, Does.Contain("Gracias por su compra"));
        });
    }

    [Test]
    public void An_invoice_shows_its_lines_taxes_withholdings_and_totals()
    {
        var html = templates.Render(PdfSamples.Invoice());

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("<th>%</th>"));
            Assert.That(html, Does.Contain("Desarrollo de software"));
            Assert.That(html, Does.Contain("Primera entrega"));
            Assert.That(html, Does.Contain("<td>IVA</td><td class=\"num\">9,200,000.00</td><td class=\"num\">19.00%</td><td class=\"num\">1,748,000.00</td>"));
            Assert.That(html, Does.Contain("<td>ReteICA</td><td class=\"num\">9,200,000.00</td><td class=\"num\">0.966%</td><td class=\"num\">88,872.00</td>"));
            Assert.That(html, Does.Contain("<td>Retenciones:</td><td class=\"num\">719,072.00</td>"));
            Assert.That(html, Does.Contain("<td>Total Factura - Descuentos:</td><td class=\"num\"><strong>10,228,928.00</strong>"));
            Assert.That(html, Does.Contain("<td>Total a Pagar</td><td class=\"num\"><strong>10,228,928.00</strong>"));
            Assert.That(html, Does.Contain("<strong>SON</strong>: DIEZ MILLONES DOSCIENTOS VEINTIOCHO MIL NOVECIENTOS VEINTIOCHO PESOS CON CERO CENTAVOS M/CTE*********."));
        });
    }

    [Test]
    public void A_credit_note_shows_its_cude_reference_and_charges_without_a_resolution()
    {
        var html = templates.Render(PdfSamples.CreditNote());

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("NOTA CREDITO DE LA FACTURA ELECTRONICA DE VENTA No"));
            Assert.That(html, Does.Contain("REPRESENTACION GRAFICA DE NOTA CREDITO ELECTRONICA"));
            Assert.That(html, Does.Contain("Nota Crédito No: NC - 1"));
            Assert.That(html, Does.Contain("CUDE: <span class=\"uuid\">"));
            Assert.That(html, Does.Contain("Referencia: SETP990000001 - Fecha: 2026-11-14"));
            Assert.That(html, Does.Contain("<td>Flete</td><td class=\"num\">35,000.00</td>"));
            Assert.That(html, Does.Contain("<td>Total Nota:</td><td class=\"num\"><strong>10,948,000.00</strong>"));
            Assert.That(html, Does.Not.Contain("Resolución de Facturación"));
            Assert.That(html, Does.Not.Contain("<th>%</th>"));
            Assert.That(html, Does.Not.Contain("título valor"));
        });
    }

    [Test]
    public void A_debit_note_has_its_own_titles()
    {
        var html = templates.Render(PdfSamples.DebitNote());

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("NOTA DEBITO DE LA FACTURA"));
            Assert.That(html, Does.Contain("REPRESENTACION GRAFICA DE NOTA DEBITO ELECTRONICA"));
            Assert.That(html, Does.Contain("Nota Debito No: ND - 1"));
        });
    }

    [Test]
    public void The_company_logo_is_shown_in_the_header_when_there_is_one()
    {
        var png = templates.Render(PdfSamples.Invoice() with { Logo = new PdfImage([0x89, 0x50], "image/png") });
        var jpeg = templates.Render(PdfSamples.CreditNote() with { Logo = new PdfImage([0xFF, 0xD8], "image/jpeg") });
        var none = templates.Render(PdfSamples.Invoice());

        Assert.Multiple(() =>
        {
            Assert.That(png, Does.Contain("<img class=\"logo\" src=\"logo.png\" alt=\"Logo\">"));
            Assert.That(jpeg, Does.Contain("<img class=\"logo\" src=\"logo.jpg\" alt=\"Logo\">"));
            Assert.That(none, Does.Not.Contain("class=\"logo\""));
        });
    }

    [Test]
    public void A_preview_carries_the_draft_mark_and_a_document_does_not()
    {
        var preview = templates.Render(PdfSamples.Invoice() with { IsPreview = true });
        var issued = templates.Render(PdfSamples.Invoice());

        Assert.Multiple(() =>
        {
            Assert.That(preview, Does.Contain("<div class=\"preview-mark\">VISTA PREVIA - SIN VALIDEZ</div>"));
            Assert.That(issued, Does.Not.Contain("preview-mark\">"));
        });
    }

    [Test]
    public void The_logo_travels_as_a_file_named_by_its_type()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DocumentHtmlTemplates.LogoFileName(new PdfImage([1], "image/png")), Is.EqualTo("logo.png"));
            Assert.That(DocumentHtmlTemplates.LogoFileName(new PdfImage([1], "image/jpeg")), Is.EqualTo("logo.jpg"));
        });
    }

    [Test]
    public void The_qr_image_is_a_png_of_the_qr_text()
    {
        var png = QrCodeImage.Png(PdfSamples.Invoice().QrText);

        Assert.That(png.Take(8), Is.EqualTo(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));
    }

    [Test]
    public void The_stylesheet_travels_with_the_templates()
    {
        Assert.That(System.Text.Encoding.UTF8.GetString(templates.Stylesheet), Does.Contain("@page"));
    }
}
