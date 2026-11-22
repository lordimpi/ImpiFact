using System.Net;
using System.Text;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Companies.UploadCertificate;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Identity;
using ImpiFact.Integration.Tests.Protection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Companies;

/// <summary>
/// Subida del certificado con un usuario del panel. Va por el caso de uso armado en el contenedor de la API (con todos
/// sus decoradores, la base, el almacén y el revisor reales) y no por HTTP, porque hoy ninguna credencial HTTP puede
/// subirlo: las API key no tienen usuario (<c>certificates.uploaded_by</c>) y los JWT del panel no traen alcances.
/// </summary>
[Category("Integration")]
public sealed class CertificateUploadTests
{
    private static CompanyApiFactory Api => CompaniesHost.Api;

    [Test]
    public async Task Certificate_is_stored_encrypted_and_only_its_reference_reaches_the_table()
    {
        var (client, nit) = await CompanyApiTests.CreateCompanyAsync();
        using var _ = client;
        var (companyId, userId) = await CompanyAndUserAsync(nit);
        var pfx = TestPfx.Rsa();

        var result = await UploadAsync(companyId, userId, pfx);

        Assert.That(result.IsSuccess, Is.True, () => result.Error.Code);
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var row = await CompanyApiTests.Rows<Certificate>(scope.Db, companyId).SingleAsync();
        var file = Directory.GetFiles(Api.CertificateDirectory, "*", SearchOption.AllDirectories)
            .Single(path => path.Contains(companyId.ToString("D"), StringComparison.OrdinalIgnoreCase));
        var stored = await File.ReadAllBytesAsync(file);
        using var loaded = await scope.Services.GetRequiredService<ICertificateStore>().LoadAsync(companyId, row.SecretRef, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(row.IsActive, Is.True);
            Assert.That(row.UploadedBy, Is.EqualTo(userId));
            Assert.That(row.Name, Is.EqualTo($"{nit}{Application.Features.Companies.Nit.CheckDigit(nit)}.p12"));
            Assert.That(row.SecretRef, Does.Not.Contain(TestPfx.Password));
            Assert.That(stored, Is.Not.EqualTo(pfx), "The file is encrypted, not the raw PKCS#12.");
            Assert.That(Encoding.UTF8.GetString(stored), Does.Not.Contain(TestPfx.Password));
            Assert.That(loaded.HasPrivateKey, Is.True);
            Assert.That(row.Thumbprint, Is.EqualTo(loaded.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256)));
            // 15-ene-2026 en Colombia hasta el 31-dic-2035 (el vencimiento 2036-01-01 00:00 UTC es la noche anterior en Colombia).
            Assert.That(result.Value.DaysLeft, Is.EqualTo(new DateOnly(2035, 12, 31).DayNumber - new DateOnly(2026, 1, 15).DayNumber));
        });
    }

    [Test]
    public async Task Second_upload_leaves_a_single_active_certificate()
    {
        var (client, nit) = await CompanyApiTests.CreateCompanyAsync();
        using var _ = client;
        var (companyId, userId) = await CompanyAndUserAsync(nit);

        Assert.That((await UploadAsync(companyId, userId, TestPfx.Rsa())).IsSuccess, Is.True);
        var second = await UploadAsync(companyId, userId, TestPfx.Rsa());

        Assert.That(second.IsSuccess, Is.True);
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var rows = await CompanyApiTests.Rows<Certificate>(scope.Db, companyId).ToListAsync();
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows.Count(row => row.IsActive), Is.EqualTo(1));
        Assert.That(rows.Single(row => row.IsActive).Thumbprint, Is.EqualTo(second.Value.Certificate.Thumbprint));
    }

    [Test]
    public async Task Listing_and_end_date_show_the_uploaded_certificate_with_its_days_left()
    {
        var (client, nit) = await CompanyApiTests.CreateCompanyAsync();
        using var _ = client;
        var (companyId, userId) = await CompanyAndUserAsync(nit);
        var notAfter = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.That((await UploadAsync(companyId, userId, TestPfx.Rsa(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), notAfter))).IsSuccess);

        using var listing = await client.GetAsync($"/api/v1/companies/{nit}/certificates");
        using var endDate = await client.GetAsync($"/api/v1/companies/{nit}/certificate/end-date");

        Assert.That(listing.StatusCode, Is.EqualTo(HttpStatusCode.OK), await listing.Content.ReadAsStringAsync());
        Assert.That(endDate.StatusCode, Is.EqualTo(HttpStatusCode.OK), await endDate.Content.ReadAsStringAsync());
        using var listingJson = await CompanyApiTests.Json(listing);
        using var endDateJson = await CompanyApiTests.Json(endDate);
        var certificate = listingJson.RootElement.GetProperty("certificates")[0];
        var body = await listing.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(certificate.GetProperty("certificate_days_left").GetInt32(), Is.EqualTo(45));
            Assert.That(certificate.GetProperty("is_active").GetBoolean(), Is.True);
            Assert.That(body, Does.Not.Contain("secret_ref").And.Not.Contain(companyId.ToString("D")));
            Assert.That(endDateJson.RootElement.GetProperty("expiration_date").GetString(), Is.EqualTo("01/03/2026"));
            Assert.That(endDateJson.RootElement.GetProperty("certificate_days_left").GetInt32(), Is.EqualTo(45));
        });
    }

    private static async Task<(Guid CompanyId, Guid UserId)> CompanyAndUserAsync(string nit)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var user = new User
        {
            Email = $"user-{nit}@example.com",
            PasswordHash = "not-a-real-hash",
            Name = "Usuario de prueba",
            SecurityStamp = Guid.NewGuid().ToString("N"),
        };
        scope.Db.Set<User>().Add(user);
        await scope.Db.SaveChangesAsync();
        return (await CompanyApiTests.CompanyIdAsync(scope.Db, nit), user.Id);
    }

    private static async Task<Result<CertificateSavedResponse>> UploadAsync(Guid companyId, Guid userId, byte[] pfx)
    {
        await using var scope = Api.OpenScope(companyId, userId, ActorType.User);
        var handler = scope.Services.GetRequiredService<IHandler<UploadCertificateCommand, CertificateSavedResponse>>();
        return await handler.HandleAsync(
            new UploadCertificateCommand { Certificate = Convert.ToBase64String(pfx), Password = TestPfx.Password }, CancellationToken.None);
    }
}
