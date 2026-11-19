using System.Net;
using System.Text;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Integration.Tests.DocumentQueries;

/// <summary>
/// Exportación CSV del listado (<c>GET /api/v1/documents/export.csv</c>): mismos filtros, encabezados en español,
/// separador <c>;</c>, importes con coma decimal y un tope de filas con error claro.
/// </summary>
[Category("Integration")]
public sealed class DocumentExportApiTests
{
    private static readonly DateTimeOffset Start = new(2026, 5, 4, 15, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task The_csv_has_spanish_headers_semicolons_colombian_amounts_and_only_the_filtered_rows_of_the_company()
    {
        var company = await DocumentSeed.CompanyAsync();
        var other = await DocumentSeed.CompanyAsync();
        await DocumentSeed.DocumentAsync(company, 1, 1, DocumentStatus.Accepted, Start, cufe: "cufe-csv-1-" + company.Nit,
            customerName: "Acme; \"La Mejor\"", customerIdentification: "900123456", emailStatus: EmailStatus.Sent);
        await DocumentSeed.DocumentAsync(company, 4, 2, DocumentStatus.Rejected, Start.AddMinutes(1), customerName: "=HYPERLINK(\"x\")");
        await DocumentSeed.DocumentAsync(company, 1, 3, DocumentStatus.Queued, Start.AddMinutes(2));
        await DocumentSeed.DocumentAsync(other, 1, 1, DocumentStatus.Accepted, Start);
        using var client = await DocumentSeed.ClientAsync(company);

        using var response = await client.GetAsync("/api/v1/documents/export.csv?status=accepted,rejected&from=2026-05-04&to=2026-05-04");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        var text = Encoding.UTF8.GetString(bytes);
        var lines = text.TrimStart('﻿').Split("\r\n");
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
            Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("text/csv"));
            Assert.That(response.Content.Headers.ContentType.CharSet, Is.EqualTo("utf-8"));
            Assert.That(response.Content.Headers.ContentDisposition!.FileName?.Trim('"'), Is.EqualTo("documentos.csv"));
            Assert.That(bytes.Take(3), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }), "UTF-8 with BOM.");
            Assert.That(lines, Is.EqualTo(new[]
            {
                "Tipo;Número;Fecha de emisión;Hora de emisión;Identificación del cliente;Cliente;Subtotal;Impuestos;Total;Estado;Estado del correo;CUFE/CUDE",
                "Nota crédito;SETP2;2026-05-04;10:01:00;222222222222;\"'=HYPERLINK(\"\"x\"\")\";100,00;19,00;119,00;Rechazado;No enviado;",
                $"Factura;SETP1;2026-05-04;10:00:00;900123456;\"Acme; \"\"La Mejor\"\"\";100,00;19,00;119,00;Aceptado;Enviado;cufe-csv-1-{company.Nit}",
                "",
            }));
        });
    }

    [Test]
    public async Task More_rows_than_the_maximum_answer_422_before_writing_anything()
    {
        var company = await DocumentSeed.CompanyAsync();
        await DocumentSeed.ManyDocumentsAsync(company, 50_001, Start);
        using var client = await DocumentSeed.ClientAsync(company);

        using var tooMany = await client.GetAsync("/api/v1/documents/export.csv");
        using var narrowed = await client.GetAsync("/api/v1/documents/export.csv?number=BULK7");

        using var json = await DocumentSeed.JsonAsync(tooMany);
        var narrowedLines = (await narrowed.Content.ReadAsStringAsync()).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Multiple(() =>
        {
            Assert.That(tooMany.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(json.RootElement.GetProperty("code").GetString(), Is.EqualTo("documents.export_too_large"));
            Assert.That(json.RootElement.GetProperty("detail").GetString(), Is.EqualTo(
                "La exportación tiene 50.001 documentos y el máximo es 50.000. Acote los filtros, por ejemplo con un rango de fechas más corto."));
            Assert.That(narrowed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(narrowedLines, Has.Length.EqualTo(2));
        });
    }

    [Test]
    public async Task The_export_needs_documents_read_and_rejects_invalid_filters()
    {
        var company = await DocumentSeed.CompanyAsync();
        using var configOnly = await DocumentSeed.ClientAsync(company, ApiScopes.ConfigWrite);
        using var reader = await DocumentSeed.ClientAsync(company);

        using var forbidden = await configOnly.GetAsync("/api/v1/documents/export.csv");
        using var invalid = await reader.GetAsync("/api/v1/documents/export.csv?kind=receipt&from=ayer");

        using var json = await DocumentSeed.JsonAsync(invalid);
        Assert.Multiple(() =>
        {
            Assert.That(forbidden.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(json.RootElement.GetProperty("errors").GetProperty("kind")[0].GetString(), Is.EqualTo("kind es inválido."));
            Assert.That(json.RootElement.GetProperty("errors").GetProperty("from")[0].GetString(), Is.EqualTo("from no es una fecha válida."));
        });
    }
}
