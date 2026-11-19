using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Companies;

/// <summary>
/// Logo de la empresa por la API (<c>PUT</c>, <c>GET</c> y <c>DELETE /api/v1/companies/{nit}/logo</c>), guardado en el
/// almacén de archivos real con su fila en <c>company_logos</c>: solo PNG o JPEG reconocidos por sus primeros bytes, hasta 512 KiB, y solo la empresa dueña
/// del NIT.
/// </summary>
[Category("Integration")]
public sealed class CompanyLogoApiTests
{
    /// <summary>Un PNG válido de 1×1 píxel.</summary>
    internal static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    /// <summary>Cabecera de un JPEG (marca SOI y segmento JFIF); basta para reconocer el formato.</summary>
    internal static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9];

    private static CompanyApiFactory Api => CompaniesHost.Api;

    [Test]
    public async Task A_png_logo_is_stored_and_served_back_byte_for_byte()
    {
        var (company, nit) = await CompanyApiTests.CreateCompanyAsync();
        using var owned = company;

        using var saved = await company.PutAsync($"/api/v1/companies/{nit}/logo", Form(Png, "logo.png", "image/png"));
        using var fetched = await company.GetAsync($"/api/v1/companies/{nit}/logo");

        using var body = await CompanyApiTests.Json(saved);
        Assert.Multiple(async () =>
        {
            Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.OK), body.RootElement.ToString());
            Assert.That(body.RootElement.GetProperty("content_type").GetString(), Is.EqualTo("image/png"));
            Assert.That(body.RootElement.GetProperty("size_bytes").GetInt32(), Is.EqualTo(Png.Length));
            Assert.That(fetched.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(fetched.Content.Headers.ContentType?.MediaType, Is.EqualTo("image/png"));
            Assert.That(await fetched.Content.ReadAsByteArrayAsync(), Is.EqualTo(Png));
            Assert.That(fetched.Headers.CacheControl?.NoCache, Is.True);
        });
    }

    [Test]
    public async Task A_jpeg_replaces_the_previous_logo_and_the_declared_type_is_ignored()
    {
        var (company, nit) = await CompanyApiTests.CreateCompanyAsync();
        using var owned = company;

        using var first = await company.PutAsync($"/api/v1/companies/{nit}/logo", Form(Png, "logo.png", "image/png"));
        using var second = await company.PutAsync($"/api/v1/companies/{nit}/logo", Form(Jpeg, "logo.bin", "application/octet-stream"));
        using var fetched = await company.GetAsync($"/api/v1/companies/{nit}/logo");

        Assert.Multiple(async () =>
        {
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(fetched.Content.Headers.ContentType?.MediaType, Is.EqualTo("image/jpeg"));
            Assert.That(await fetched.Content.ReadAsByteArrayAsync(), Is.EqualTo(Jpeg));
        });
    }

    [Test]
    public async Task Without_a_logo_the_read_answers_404_and_the_delete_is_idempotent()
    {
        var (company, nit) = await CompanyApiTests.CreateCompanyAsync();
        using var owned = company;

        using var missing = await company.GetAsync($"/api/v1/companies/{nit}/logo");
        using var saved = await company.PutAsync($"/api/v1/companies/{nit}/logo", Form(Png, "logo.png", "image/png"));
        using var deleted = await company.DeleteAsync($"/api/v1/companies/{nit}/logo");
        using var again = await company.DeleteAsync($"/api/v1/companies/{nit}/logo");
        using var gone = await company.GetAsync($"/api/v1/companies/{nit}/logo");

        using var missingBody = await CompanyApiTests.Json(missing);
        Assert.Multiple(() =>
        {
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(missingBody.RootElement.GetProperty("code").GetString(), Is.EqualTo("company.logo_not_found"));
            Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(gone.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [TestCase("svg", TestName = "An_svg_logo_answers_422")]
    [TestCase("gif", TestName = "A_gif_logo_answers_422")]
    [TestCase("empty", TestName = "An_empty_logo_answers_422")]
    [TestCase("large", TestName = "A_logo_over_512_KiB_answers_422")]
    public async Task A_logo_that_is_not_a_small_png_or_jpeg_answers_422(string kind)
    {
        var (company, nit) = await CompanyApiTests.CreateCompanyAsync();
        using var owned = company;
        var content = kind switch
        {
            "svg" => "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray(),
            "gif" => "GIF89a\u0001\u0000\u0001\u0000"u8.ToArray(),
            "empty" => [],
            _ => [.. Png, .. new byte[(512 * 1024) - Png.Length + 1]],
        };

        using var response = await company.PutAsync($"/api/v1/companies/{nit}/logo", Form(content, "logo.png", "image/png"));
        using var missing = await company.GetAsync($"/api/v1/companies/{nit}/logo");

        using var body = await CompanyApiTests.Json(response);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), body.RootElement.ToString());
            Assert.That(body.RootElement.GetProperty("errors").TryGetProperty("logo", out _), Is.True, body.RootElement.ToString());
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "A refused logo is not stored.");
        });
    }

    [Test]
    public async Task A_form_without_the_logo_field_answers_422()
    {
        var (company, nit) = await CompanyApiTests.CreateCompanyAsync();
        using var owned = company;
        using var form = new MultipartFormDataContent { { new StringContent("x"), "other" } };

        using var response = await company.PutAsync($"/api/v1/companies/{nit}/logo", form);

        using var body = await CompanyApiTests.Json(response);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), body.RootElement.ToString());
    }

    [Test]
    public async Task Another_company_nit_answers_403_and_changes_nothing()
    {
        var (company, nit) = await CompanyApiTests.CreateCompanyAsync();
        var (other, otherNit) = await CompanyApiTests.CreateCompanyAsync();
        using var owned = company;
        using var ownedOther = other;
        using var saved = await other.PutAsync($"/api/v1/companies/{otherNit}/logo", Form(Png, "logo.png", "image/png"));

        using var put = await company.PutAsync($"/api/v1/companies/{otherNit}/logo", Form(Jpeg, "logo.jpg", "image/jpeg"));
        using var get = await company.GetAsync($"/api/v1/companies/{otherNit}/logo");
        using var delete = await company.DeleteAsync($"/api/v1/companies/{otherNit}/logo");
        using var stillThere = await other.GetAsync($"/api/v1/companies/{otherNit}/logo");

        Assert.Multiple(async () =>
        {
            Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(await stillThere.Content.ReadAsByteArrayAsync(), Is.EqualTo(Png));
        });
    }

    [Test]
    public async Task Reading_needs_documents_read_and_changing_needs_config_write()
    {
        var (company, nit) = await CompanyApiTests.CreateCompanyAsync();
        using var owned = company;
        using var saved = await company.PutAsync($"/api/v1/companies/{nit}/logo", Form(Png, "logo.png", "image/png"));
        Guid companyId;
        await using (var scope = Api.OpenScope(null, null, ActorType.System))
        {
            companyId = await CompanyApiTests.CompanyIdAsync(scope.Db, nit);
        }

        using var reader = await Api.ClientWithNewKeyAsync(companyId, ApiScopes.DocumentsRead);
        using var read = await reader.GetAsync($"/api/v1/companies/{nit}/logo");
        using var put = await reader.PutAsync($"/api/v1/companies/{nit}/logo", Form(Jpeg, "logo.jpg", "image/jpeg"));
        using var delete = await reader.DeleteAsync($"/api/v1/companies/{nit}/logo");

        Assert.Multiple(() =>
        {
            Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        });
    }

    [Test]
    public async Task The_logo_row_points_to_its_file_in_the_storage_and_the_delete_removes_both()
    {
        var (company, nit) = await CompanyApiTests.CreateCompanyAsync();
        using var owned = company;
        using var first = await company.PutAsync($"/api/v1/companies/{nit}/logo", Form(Png, "logo.png", "image/png"));
        var firstRow = await LogoRowAsync(nit);
        using var second = await company.PutAsync($"/api/v1/companies/{nit}/logo", Form(Jpeg, "logo.jpg", "image/jpeg"));
        var secondRow = await LogoRowAsync(nit);
        var storage = Api.Services.GetRequiredService<IBlobStorage>();
        var stored = await ReadAsync(storage, secondRow!.StorageKey);

        using var deleted = await company.DeleteAsync($"/api/v1/companies/{nit}/logo");
        var afterDelete = await LogoRowAsync(nit);

        Assert.Multiple(() =>
        {
            Assert.That(firstRow!.ContentType, Is.EqualTo("image/png"));
            Assert.That(firstRow.StorageKey, Does.EndWith(".png"));
            Assert.That(secondRow.ContentType, Is.EqualTo("image/jpeg"));
            Assert.That(secondRow.StorageKey, Does.StartWith($"{secondRow.CompanyId:N}/company/logo-").And.EndWith(".jpg"));
            Assert.That(secondRow.StorageKey, Is.Not.EqualTo(firstRow.StorageKey), "A new upload never overwrites the file the row pointed to.");
            Assert.That(stored, Is.EqualTo(Jpeg));
            Assert.That(afterDelete, Is.Null);
            Assert.That(async () => await ReadAsync(storage, secondRow.StorageKey), Throws.TypeOf<BlobNotFoundException>());
            Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        });
    }

    private static async Task<Domain.Companies.CompanyLogo?> LogoRowAsync(string nit)
    {
        await using var scope = Api.OpenScope(null, null, ActorType.System);
        var companyId = await CompanyApiTests.CompanyIdAsync(scope.Db, nit);
        return await scope.Db.Set<Domain.Companies.CompanyLogo>().IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(row => row.CompanyId == companyId);
    }

    private static async Task<byte[]> ReadAsync(IBlobStorage storage, string key)
    {
        await using var stream = await storage.OpenReadAsync(key, CancellationToken.None);
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        return copy.ToArray();
    }

    internal static MultipartFormDataContent Form(byte[] content, string fileName, string contentType)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return new MultipartFormDataContent { { file, "logo", fileName } };
    }
}
