using ImpiFact.Application.Abstractions.Billing;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Platform.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static ImpiFact.Integration.Tests.DocumentUtilities.UtilitiesHost;

namespace ImpiFact.Integration.Tests.DocumentUtilities;

/// <summary>
/// Nombres de archivo de <c>download</c>: solo los nombres exactos del PHP se sirven; un salto de línea al final o un
/// intento de salir de la carpeta responde 404.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class DownloadNameTests
{
    [Test]
    public async Task A_file_name_with_a_trailing_line_break_is_not_found()
    {
        var company = await CompanyAsync(withCertificate: false);
        var client = await ClientAsync(company, ApiScopes.DocumentsRead);
        var document = await DocumentAsync(company);
        await FileAsync(document, DocumentFileKind.Pdf, "%PDF-1.7"u8.ToArray(), "application/pdf");

        using var response = await client.GetAsync($"/api/ubl2.1/download/{company.Nit}/FES-{document.FullNumber}.pdf%0A");
        TestContext.Out.WriteLine($"status {(int)response.StatusCode}; disposition {response.Content.Headers.ContentDisposition}");
        Assert.That((int)response.StatusCode, Is.EqualTo(404));
    }

    [TestCase("..%2F..%2Fappsettings.json")]
    [TestCase("FES-..%2F1.pdf")]
    public async Task Traversal_like_names_are_not_found(string file)
    {
        var company = await CompanyAsync(withCertificate: false);
        var client = await ClientAsync(company, ApiScopes.DocumentsRead);
        using var response = await client.GetAsync($"/api/ubl2.1/download/{company.Nit}/{file}");
        Assert.That((int)response.StatusCode, Is.AnyOf(404, 400));
    }
}
