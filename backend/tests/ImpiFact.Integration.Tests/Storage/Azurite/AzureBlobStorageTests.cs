using System.Net;
using System.Text;
using System.Web;
using Azure;
using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Domain.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ImpiFact.Integration.Tests.Storage.Azurite;

/// <summary>
/// Proveedor <c>AzureBlob</c> contra Azurite: guarda y lee por clave con su tipo de contenido, reemplaza, acepta flujos
/// que no se rebobinan, informa «no existe» con <see cref="BlobNotFoundException"/>, borra sin fallar si ya no existe,
/// firma URL de descarga (SAS de solo lectura) que vencen, rechaza las mismas claves que el local y S3, y la prueba de
/// salud distingue un contenedor que existe de uno que falta.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class AzureBlobStorageTests
{
    private static IBlobStorage Storage => AzuriteStorage.Services.GetRequiredService<IBlobStorage>();

    [Test]
    public async Task A_stored_blob_is_read_back_with_its_bytes_and_content_type()
    {
        var key = NewKey(".xml");

        var stored = await Storage.PutAsync(key, Content("<Invoice/>"), "application/xml", CancellationToken.None);
        var read = await ReadAsync(key);
        var properties = await AzuriteStorage.Client.GetBlobContainerClient(AzuriteStorage.Container).GetBlobClient(key).GetPropertiesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(stored, Is.EqualTo(key));
            Assert.That(read, Is.EqualTo("<Invoice/>"));
            Assert.That(properties.Value.ContentType, Is.EqualTo("application/xml"));
            Assert.That(Storage.Provider, Is.EqualTo(StorageProvider.AzureBlob));
        });
    }

    [Test]
    public async Task Storing_again_under_the_same_key_replaces_the_content()
    {
        var key = NewKey(".xml");
        await Storage.PutAsync(key, Content("first version, longer"), "application/xml", CancellationToken.None);

        await Storage.PutAsync(key, Content("second"), "application/xml", CancellationToken.None);

        Assert.That(await ReadAsync(key), Is.EqualTo("second"));
    }

    [Test]
    public async Task A_stream_that_cannot_seek_is_stored_completely()
    {
        var key = NewKey(".pdf");
        var bytes = new byte[300_000];
        Random.Shared.NextBytes(bytes);

        await Storage.PutAsync(key, new ForwardOnlyStream(bytes), "application/pdf", CancellationToken.None);

        await using var stream = await Storage.OpenReadAsync(key, CancellationToken.None);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        Assert.That(buffer.ToArray(), Is.EqualTo(bytes));
    }

    [Test]
    public void Reading_a_missing_key_reports_it_as_not_found()
    {
        var key = NewKey(".xml");

        var exception = Assert.ThrowsAsync<BlobNotFoundException>(() => Storage.OpenReadAsync(key, CancellationToken.None));

        Assert.That(exception!.Key, Is.EqualTo(key));
    }

    [Test]
    public async Task Deleting_removes_the_blob_and_deleting_again_does_nothing()
    {
        var key = NewKey(".xml");
        await Storage.PutAsync(key, Content("x"), "application/xml", CancellationToken.None);

        await Storage.DeleteAsync(key, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<BlobNotFoundException>(() => Storage.OpenReadAsync(key, CancellationToken.None));
            Assert.DoesNotThrowAsync(() => Storage.DeleteAsync(key, CancellationToken.None));
        });
    }

    // El contenedor lo crea el despliegue: si falta, no es «el archivo no existe» sino un almacén mal configurado.
    [Test]
    public void A_missing_container_is_not_reported_as_a_missing_file()
    {
        using var services = AzuriteStorage.BuildStorage(AzuriteStorage.StorageSettings("missing-container"));
        var storage = services.GetRequiredService<IBlobStorage>();

        var exception = Assert.ThrowsAsync<RequestFailedException>(() => storage.OpenReadAsync(NewKey(".xml"), CancellationToken.None));

        Assert.That(exception!.ErrorCode, Is.EqualTo("ContainerNotFound"));
    }

    [Test]
    public async Task The_readiness_check_is_healthy_with_the_container_and_unhealthy_without_it()
    {
        using var missing = AzuriteStorage.BuildStorage(AzuriteStorage.StorageSettings("missing-container"));

        var up = await CheckAsync(Storage);
        var down = await CheckAsync(missing.GetRequiredService<IBlobStorage>());

        Assert.Multiple(() =>
        {
            Assert.That(up.Status, Is.EqualTo(HealthStatus.Healthy));
            Assert.That(down.Status, Is.EqualTo(HealthStatus.Unhealthy));
        });
    }

    [Test]
    public async Task A_signed_url_downloads_the_blob_over_http_without_other_credentials()
    {
        var key = NewKey(".pdf");
        await Storage.PutAsync(key, Content("%PDF-1.7"), "application/pdf", CancellationToken.None);

        var url = Storage.CreateSignedUrl(key, TimeSpan.FromMinutes(5));
        using var http = new HttpClient();
        using var response = await http.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        var query = HttpUtility.ParseQueryString(url.Query);

        Assert.Multiple(() =>
        {
            Assert.That(query["sig"], Is.Not.Null.And.Not.Empty);
            Assert.That(query["sp"], Is.EqualTo("r"), "Read only.");
            Assert.That(query["sr"], Is.EqualTo("b"), "One blob only.");
            Assert.That(DateTimeOffset.Parse(query["se"]!, System.Globalization.CultureInfo.InvariantCulture),
                Is.EqualTo(DateTimeOffset.UtcNow.AddMinutes(5)).Within(TimeSpan.FromSeconds(30)));
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/pdf"));
            Assert.That(body, Is.EqualTo("%PDF-1.7"));
        });
    }

    [Test]
    public async Task A_signed_url_stops_working_after_it_expires()
    {
        var key = NewKey(".pdf");
        await Storage.PutAsync(key, Content("%PDF-1.7"), "application/pdf", CancellationToken.None);

        var url = Storage.CreateSignedUrl(key, TimeSpan.FromSeconds(2));
        await Task.Delay(TimeSpan.FromSeconds(4));
        using var http = new HttpClient();
        using var response = await http.GetAsync(url);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [TestCase(0, TestName = "AzureBlob_a_signed_url_without_lifetime_is_rejected")]
    [TestCase(-60, TestName = "AzureBlob_a_signed_url_with_negative_lifetime_is_rejected")]
    [TestCase(3601, TestName = "AzureBlob_a_signed_url_longer_than_the_configured_limit_is_rejected")]
    public void Signed_url_lifetimes_are_bounded(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Storage.CreateSignedUrl(NewKey(".pdf"), TimeSpan.FromSeconds(seconds)));
    }

    [TestCase("", TestName = "AzureBlob_an_empty_key_is_rejected")]
    [TestCase("/etc/passwd", TestName = "AzureBlob_an_absolute_key_is_rejected")]
    [TestCase("../outside.xml", TestName = "AzureBlob_a_key_that_climbs_out_is_rejected")]
    [TestCase("a/./b.xml", TestName = "AzureBlob_a_key_with_a_dot_segment_is_rejected")]
    [TestCase("a\\..\\b.xml", TestName = "AzureBlob_a_key_with_backslashes_is_rejected")]
    [TestCase("a//b.xml", TestName = "AzureBlob_a_key_with_an_empty_segment_is_rejected")]
    [TestCase("a/b.xml?x=1", TestName = "AzureBlob_a_key_with_a_query_is_rejected")]
    public void Keys_with_an_invalid_shape_are_rejected_for_every_operation(string key)
    {
        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<ArgumentException>(() => Storage.PutAsync(key, Content("x"), "application/xml", CancellationToken.None));
            Assert.ThrowsAsync<ArgumentException>(() => Storage.OpenReadAsync(key, CancellationToken.None));
            Assert.ThrowsAsync<ArgumentException>(() => Storage.DeleteAsync(key, CancellationToken.None));
            Assert.Throws<ArgumentException>(() => Storage.CreateSignedUrl(key, TimeSpan.FromMinutes(1)));
        });
    }

    private static Task<HealthReportEntry> CheckAsync(IBlobStorage storage) => StorageReadiness.CheckAsync(storage);

    private static string NewKey(string extension) => $"{Guid.NewGuid():N}/documents/{Guid.NewGuid():N}/{Guid.NewGuid():N}{extension}";

    private static MemoryStream Content(string text) => new(Encoding.UTF8.GetBytes(text));

    private static async Task<string> ReadAsync(string key)
    {
        await using var stream = await Storage.OpenReadAsync(key, CancellationToken.None);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}
