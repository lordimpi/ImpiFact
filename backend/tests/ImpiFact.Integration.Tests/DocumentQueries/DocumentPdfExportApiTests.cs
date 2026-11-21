using System.Net;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Documents;
using ImpiFact.Integration.Tests.DocumentIssue;

namespace ImpiFact.Integration.Tests.DocumentQueries;

/// <summary>
/// PDF report of the list (<c>GET /api/v1/documents/export.pdf</c>): the same filters and rows as the CSV and Excel
/// exports, rendered as HTML and converted by Gotenberg (a fake here), with its own lower row cap.
/// </summary>
[Category("Integration")]
public sealed class DocumentPdfExportApiTests
{
    private static readonly DateTimeOffset Start = new(2026, 5, 4, 15, 0, 0, TimeSpan.Zero);

    private static FakeGotenberg Gotenberg => DocumentQueriesHost.Api.Gotenberg;

    [TearDown]
    public void GotenbergUp() => Gotenberg.Down = false;

    [Test]
    public async Task The_pdf_is_the_converted_report_of_only_the_filtered_rows_of_the_company()
    {
        var company = await DocumentSeed.CompanyAsync();
        var other = await DocumentSeed.CompanyAsync();
        await DocumentSeed.DocumentAsync(company, 1, 1, DocumentStatus.Accepted, Start, cufe: "cufe-pdf-1-" + company.Nit,
            customerName: "<script>alert('x')</script> & Hijos", customerIdentification: "900123456", emailStatus: EmailStatus.Sent);
        await DocumentSeed.DocumentAsync(company, 4, 2, DocumentStatus.Rejected, Start.AddMinutes(1), customerName: "Acme Ltda");
        await DocumentSeed.DocumentAsync(company, 1, 3, DocumentStatus.Queued, Start.AddMinutes(2), customerName: "Fuera del filtro");
        await DocumentSeed.DocumentAsync(other, 1, 1, DocumentStatus.Accepted, Start, customerName: "Otra empresa");
        using var client = await DocumentSeed.ClientAsync(company);

        using var response = await client.GetAsync("/api/v1/documents/export.pdf?status=accepted,rejected&from=2026-05-04&to=2026-05-04");

        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), System.Text.Encoding.UTF8.GetString(body));
        var conversion = Gotenberg.ConversionsWith(company.Name).Single();
        Assert.Multiple(() =>
        {
            Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/pdf"));
            Assert.That(response.Content.Headers.ContentDisposition!.DispositionType, Is.EqualTo("attachment"));
            Assert.That(response.Content.Headers.ContentDisposition.FileName?.Trim('"'), Is.EqualTo("documentos.pdf"));
            Assert.That(body, Is.EqualTo(FakeGotenberg.Pdf));
            Assert.That(conversion.Path, Does.EndWith("/forms/chromium/convert/html"));
            Assert.That(conversion.Files.Keys, Is.EquivalentTo(new[] { "index.html", "footer.html" }));
            Assert.That(conversion.Html, Does.Contain(company.Name));
            Assert.That(conversion.Html, Does.Contain("NIT " + company.Nit));
            Assert.That(conversion.Html, Does.Contain("<h1>Documentos</h1>"));
            Assert.That(conversion.Html, Does.Contain("<dt>Fechas de emisión</dt><dd>04/05/2026</dd>"));
            Assert.That(conversion.Html, Does.Contain("<dt>Estados</dt><dd>Aceptado, Rechazado</dd>"));
            Assert.That(conversion.Html, Does.Contain("<dt>Tipo</dt><dd>Todos</dd>"));
            Assert.That(conversion.Html, Does.Contain("Generado el 15/01/2026 a las 05:00 (hora de Colombia)"));
            Assert.That(conversion.Html, Does.Contain("<td>&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt; &amp; Hijos</td>"));
            Assert.That(conversion.Html, Does.Not.Contain("<script>"));
            Assert.That(conversion.Html, Does.Contain("<td>Acme Ltda</td>"));
            Assert.That(conversion.Html, Does.Contain("<td class=\"amount\">119,00</td>"));
            Assert.That(conversion.Html, Does.Contain("<p class=\"count\">Total de documentos: 2</p>"));
            Assert.That(conversion.Html, Does.Not.Contain("Fuera del filtro"));
            Assert.That(conversion.Html, Does.Not.Contain("Otra empresa"), "Another company's documents are not exported.");
            Assert.That(Footer(conversion), Does.Contain("Página <span class=\"pageNumber\"></span> de <span class=\"totalPages\"></span>"));
        });
    }

    [Test]
    public async Task More_rows_than_the_pdf_maximum_answer_422_before_rendering_anything()
    {
        var company = await DocumentSeed.CompanyAsync();
        await DocumentSeed.ManyDocumentsAsync(company, 5_001, Start);
        using var client = await DocumentSeed.ClientAsync(company);

        using var tooMany = await client.GetAsync("/api/v1/documents/export.pdf");
        var conversionsAfterTooMany = Gotenberg.ConversionsWith(company.Name).Count;
        using var narrowed = await client.GetAsync("/api/v1/documents/export.pdf?number=BULK7");

        using var json = await DocumentSeed.JsonAsync(tooMany);
        Assert.Multiple(() =>
        {
            Assert.That(tooMany.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(json.RootElement.GetProperty("code").GetString(), Is.EqualTo("documents.export_too_large"));
            Assert.That(json.RootElement.GetProperty("detail").GetString(), Is.EqualTo(
                "El PDF tendría 5.001 documentos y el máximo es 5.000. Descárguelos en Excel o acote los filtros, por ejemplo con un rango de fechas más corto."));
            Assert.That(conversionsAfterTooMany, Is.Zero);
            Assert.That(narrowed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Gotenberg.ConversionsWith(company.Name).Single().Html, Does.Contain("Total de documentos: 1"));
        });
    }

    [Test]
    public async Task A_converter_failure_answers_503_pdf_unavailable_like_the_preview()
    {
        var company = await DocumentSeed.CompanyAsync();
        using var client = await DocumentSeed.ClientAsync(company);
        Gotenberg.Down = true;

        using var response = await client.GetAsync("/api/v1/documents/export.pdf");

        using var json = await DocumentSeed.JsonAsync(response);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(json.RootElement.GetProperty("code").GetString(), Is.EqualTo("pdf.unavailable"));
            Assert.That(response.Headers.RetryAfter, Is.Not.Null);
        });
    }

    [Test]
    public async Task The_export_needs_documents_read_and_rejects_invalid_filters()
    {
        var company = await DocumentSeed.CompanyAsync();
        using var configOnly = await DocumentSeed.ClientAsync(company, ApiScopes.ConfigWrite);
        using var reader = await DocumentSeed.ClientAsync(company);
        using var anonymous = DocumentQueriesHost.Api.CreateClient();

        using var forbidden = await configOnly.GetAsync("/api/v1/documents/export.pdf");
        using var unauthenticated = await anonymous.GetAsync("/api/v1/documents/export.pdf");
        using var invalid = await reader.GetAsync("/api/v1/documents/export.pdf?kind=receipt&from=ayer");

        using var json = await DocumentSeed.JsonAsync(invalid);
        Assert.Multiple(() =>
        {
            Assert.That(forbidden.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(unauthenticated.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(json.RootElement.GetProperty("errors").GetProperty("kind")[0].GetString(), Is.EqualTo("kind es inválido."));
            Assert.That(json.RootElement.GetProperty("errors").GetProperty("from")[0].GetString(), Is.EqualTo("from no es una fecha válida."));
            Assert.That(Gotenberg.ConversionsWith(company.Name), Is.Empty);
        });
    }

    private static string Footer(GotenbergConversion conversion) =>
        System.Text.Encoding.UTF8.GetString(conversion.Files["footer.html"].Content);
}
