using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Abstractions.Pdf;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Pdf.Gotenberg;

/// <summary>
/// El PDF de punta a punta: las plantillas reales convertidas por un Gotenberg real, y el mensaje <c>RenderPdf</c>
/// procesado por el Worker contra PostgreSQL.
/// </summary>
/// <remarks>
/// Si la variable de entorno <c>IMPIFACT_PDF_SAMPLE_DIR</c> apunta a una carpeta, la prueba de conversión deja ahí los
/// PDF de ejemplo (factura, nota crédito y nota débito) para revisarlos a ojo. Por defecto no escribe nada.
/// </remarks>
[TestFixture]
[Category("Integration")]
public sealed class RenderPdfTests
{
    private static IEnumerable<TestCaseData> Samples()
    {
        yield return new TestCaseData(PdfSamples.Invoice(), "invoice.pdf").SetArgDisplayNames("invoice");
        yield return new TestCaseData(PdfSamples.CreditNote(), "credit-note.pdf").SetArgDisplayNames("credit-note");
        yield return new TestCaseData(PdfSamples.DebitNote(), "debit-note.pdf").SetArgDisplayNames("debit-note");
        yield return new TestCaseData(
                PdfSamples.Invoice() with { Logo = new PdfImage(Companies.CompanyLogoApiTests.Png, "image/png") }, "invoice-with-logo.pdf")
            .SetArgDisplayNames("invoice-with-logo");
        yield return new TestCaseData(PdfSamples.Invoice() with { IsPreview = true }, "invoice-preview.pdf").SetArgDisplayNames("invoice-preview");
    }

    [TestCaseSource(nameof(Samples))]
    public async Task Gotenberg_converts_the_rendered_document_into_a_pdf(DocumentPdfModel model, string sampleName)
    {
        await using var host = PdfHost.Create();
        await using var scope = host.OpenScope();

        var pdf = await scope.ServiceProvider.GetRequiredService<IPdfRenderer>().RenderAsync(model, CancellationToken.None);

        Assert.That(pdf.Take(5), Is.EqualTo("%PDF-"u8.ToArray()));
        Assert.That(pdf, Has.Length.GreaterThan(5000), "A real page with text and the QR image, not an empty document.");
        if (Environment.GetEnvironmentVariable("IMPIFACT_PDF_SAMPLE_DIR") is { Length: > 0 } sampleDirectory)
        {
            Directory.CreateDirectory(sampleDirectory);
            await File.WriteAllBytesAsync(Path.Combine(sampleDirectory, sampleName), pdf);
        }
    }

    [Test]
    public async Task The_worker_stores_one_pdf_and_enqueues_the_email()
    {
        await using var host = await PdfHost.StartAsync();
        var document = await host.CreateAcceptedInvoiceAsync();
        await host.EnqueueRenderPdfAsync(document);

        var taken = await host.RunOutboxAsync();

        var pdfs = await host.FilesAsync(document.Id, DocumentFileKind.Pdf);
        var render = await host.MessagesAsync(document.CompanyId, OutboxMessageTypes.RenderPdf);
        var emails = await host.MessagesAsync(document.CompanyId, OutboxMessageTypes.SendEmail);
        Assert.Multiple(() =>
        {
            Assert.That(taken, Is.EqualTo(1));
            Assert.That(render.Single().Status, Is.EqualTo(OutboxStatus.Done), render.Single().LastError);
            Assert.That(pdfs, Has.Count.EqualTo(1));
            Assert.That(pdfs.Single().ContentType, Is.EqualTo("application/pdf"));
            Assert.That(pdfs.Single().Revision, Is.EqualTo(1));
            Assert.That(emails, Has.Count.EqualTo(1));
            Assert.That(emails.Single().Status, Is.EqualTo(OutboxStatus.Pending));
            Assert.That(emails.Single().Payload["document_id"]!.GetValue<Guid>(), Is.EqualTo(document.Id));
        });
        Assert.That((await host.ReadAsync(pdfs.Single())).Take(5), Is.EqualTo("%PDF-"u8.ToArray()));
    }

    [Test]
    public async Task A_repeated_render_pdf_message_keeps_one_pdf_and_one_email()
    {
        await using var host = await PdfHost.StartAsync();
        var document = await host.CreateAcceptedInvoiceAsync();
        await host.EnqueueRenderPdfAsync(document);
        await host.RunOutboxAsync();

        await host.EnqueueRenderPdfAsync(document);
        await host.RunOutboxAsync();

        var render = await host.MessagesAsync(document.CompanyId, OutboxMessageTypes.RenderPdf);
        Assert.Multiple(async () =>
        {
            Assert.That(render.Select(message => message.Status), Is.All.EqualTo(OutboxStatus.Done));
            Assert.That(await host.FilesAsync(document.Id, DocumentFileKind.Pdf), Has.Count.EqualTo(1));
            Assert.That(await host.MessagesAsync(document.CompanyId, OutboxMessageTypes.SendEmail), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task The_worker_composition_registers_the_render_pdf_handler()
    {
        await using var host = PdfHost.Create();
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(Guid.CreateVersion7(), userId: null, ActorType.System);

        var handlers = scope.ServiceProvider.GetServices<IOutboxMessageHandler>().Select(handler => handler.MessageType);

        Assert.That(handlers, Has.One.EqualTo(OutboxMessageTypes.RenderPdf));
    }
}
