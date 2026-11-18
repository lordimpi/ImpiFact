using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Pdf;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Application.Features.Documents.Pdf;
using ImpiFact.Application.Tests.Features.Documents.Issue;

namespace ImpiFact.Application.Tests.Features.Documents.Pdf;

/// <summary>
/// El modelo de la vista previa sale de la solicitud sin emitir, con lo mismo que tendrá el PDF del documento emitido
/// (líneas, impuestos, retenciones, totales, resolución, empresa y logo), pero con número, CUFE y QR de muestra y la marca
/// de vista previa.
/// </summary>
public sealed class PreviewModelTests
{
    private static readonly DocumentTotals WithholdingsTotals = new(9200000m, 9200000m, 10948000m, 0m, 0m, 0m, 10948000m);

    [Test]
    public async Task A_preview_prints_the_request_like_the_issued_document_but_with_a_draft_number_and_no_cufe()
    {
        var scenario = new PdfScenario().WithGolden("invoice/withholdings-renta-iva-ica", 1, "SETP", 990000005, WithholdingsTotals);
        scenario.Logos.Logos[PdfScenario.CompanyId] = new CompanyLogoImage([0xFF, 0xD8, 0xFF], "image/jpeg");
        var issued = await scenario.BuildAsync();
        var issuer = await scenario.Issuers.GetAsync(PdfScenario.CompanyId, 1, Domain.Common.DocumentKind.Invoice, CancellationToken.None);
        var request = await File.ReadAllBytesAsync(Path.Combine(GoldenFiles.Root, "invoice", "withholdings-renta-iva-ica", "input.json"));
        Assert.That(IssueDocumentJson.TryParse(request, out var body), Is.True);

        var preview = await scenario.Builder().BuildPreviewAsync(
            issuer!, issuer!.Resolutions[0], body!, new DateOnly(2026, 11, 14), new TimeOnly(10, 20, 0), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(preview.IsPreview, Is.True);
            Assert.That(preview.Kind, Is.EqualTo(PdfDocumentKind.Invoice));
            Assert.That(preview.Prefix, Is.EqualTo("SETP"));
            Assert.That(preview.Number, Is.EqualTo(DocumentPdfModelBuilder.PreviewNumber));
            Assert.That(preview.Uuid, Is.EqualTo(DocumentPdfModelBuilder.PreviewUuid));
            Assert.That(preview.QrText, Is.EqualTo(DocumentPdfModelBuilder.PreviewQrText));
            Assert.That(preview.ValidationDate, Is.EqualTo(DocumentPdfModelBuilder.PreviewValidation));
            Assert.That(preview.IssueDate, Is.EqualTo("2026-11-14"));
            Assert.That(preview.Resolution, Is.EqualTo(issued.Resolution));
            Assert.That(preview.Issuer, Is.EqualTo(issued.Issuer));
            Assert.That(preview.Lines, Is.EqualTo(issued.Lines));
            Assert.That(preview.Taxes, Is.EqualTo(issued.Taxes));
            Assert.That(preview.Withholdings, Is.EqualTo(issued.Withholdings));
            Assert.That(preview.Totals, Is.EqualTo(issued.Totals));
            Assert.That(preview.AmountInWords, Is.EqualTo(issued.AmountInWords));
            Assert.That(preview.Payments, Is.EqualTo(issued.Payments));
            Assert.That(preview.Logo?.ContentType, Is.EqualTo("image/jpeg"));
            Assert.That(issued.IsPreview, Is.False);
        });
    }
}
