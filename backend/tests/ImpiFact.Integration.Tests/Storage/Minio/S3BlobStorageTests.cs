using System.Net;
using System.Text;
using Amazon;
using Amazon.S3;
using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Domain.Documents;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Storage.Minio;

/// <summary>
/// Proveedor S3 contra MinIO: guarda y lee por clave con su tipo de contenido, cifra del lado del servidor (SSE-S3,
/// con AES-256) por omisión, borra sin fallar si ya no existe, firma URL de descarga que vencen (URL prefirmada:
/// un enlace temporal que da acceso a un solo archivo sin otra credencial) y rechaza las mismas claves que el local.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class S3BlobStorageTests
{
    private static IBlobStorage Storage => MinioStorage.Services.GetRequiredService<IBlobStorage>();

    [Test]
    public async Task A_stored_object_is_read_back_with_its_bytes_content_type_and_server_side_encryption()
    {
        var key = NewKey(".xml");

        var stored = await Storage.PutAsync(key, Content("<Invoice/>"), "application/xml", CancellationToken.None);
        var read = await ReadAsync(key);
        var metadata = await MinioStorage.Client.GetObjectMetadataAsync(MinioStorage.Bucket, key);

        Assert.Multiple(() =>
        {
            Assert.That(stored, Is.EqualTo(key));
            Assert.That(read, Is.EqualTo("<Invoice/>"));
            Assert.That(metadata.Headers.ContentType, Is.EqualTo("application/xml"));
            Assert.That(metadata.ServerSideEncryptionMethod, Is.EqualTo(ServerSideEncryptionMethod.AES256));
            Assert.That(Storage.Provider, Is.EqualTo(StorageProvider.S3));
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

    // Quien llama puede pasar un flujo que no se puede rebobinar (por ejemplo, el cuerpo de una respuesta HTTP).
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
    public async Task Server_side_encryption_can_be_turned_off()
    {
        var settings = MinioStorage.StorageSettings();
        settings["Storage:S3:ServerSideEncryption"] = "false";
        using var services = MinioStorage.BuildStorage(settings);
        var key = NewKey(".xml");

        await services.GetRequiredService<IBlobStorage>().PutAsync(key, Content("x"), "application/xml", CancellationToken.None);
        var metadata = await MinioStorage.Client.GetObjectMetadataAsync(MinioStorage.Bucket, key);

        Assert.That(metadata.ServerSideEncryptionMethod, Is.Null.Or.EqualTo(ServerSideEncryptionMethod.None));
    }

    [Test]
    public void Reading_a_missing_key_reports_it_as_not_found()
    {
        var key = NewKey(".xml");

        var exception = Assert.ThrowsAsync<BlobNotFoundException>(() => Storage.OpenReadAsync(key, CancellationToken.None));

        Assert.That(exception!.Key, Is.EqualTo(key));
    }

    [Test]
    public async Task Deleting_removes_the_object_and_deleting_again_does_nothing()
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

    [Test]
    public async Task A_signed_url_downloads_the_object_over_http_without_other_credentials()
    {
        var key = NewKey(".pdf");
        await Storage.PutAsync(key, Content("%PDF-1.7"), "application/pdf", CancellationToken.None);

        var url = Storage.CreateSignedUrl(key, TimeSpan.FromMinutes(5));
        using var http = new HttpClient();
        using var response = await http.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        // Esta prueba falló a veces solo con toda la suite corriendo; el detalle de la respuesta queda en el mensaje.
        var detail = $"{(int)response.StatusCode} {response.Content.Headers.ContentType} {body}";

        Assert.Multiple(() =>
        {
            Assert.That(url.Query, Does.Contain("X-Amz-Signature="));
            // Nunca dura más de lo pedido. Puede durar unos segundos menos: el SDK toma su propia hora al firmar y, con
            // el equipo cargado (por ejemplo, con toda la suite corriendo), entre las dos horas puede pasar más de un
            // segundo.
            Assert.That(ExpiresSeconds(url), Is.InRange(295, 300), url.Query);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), detail);
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/pdf"), detail);
            Assert.That(body, Is.EqualTo("%PDF-1.7"), detail);
        });
    }

    [Test]
    [NonParallelizable]
    public void A_signed_url_lasts_exactly_the_requested_lifetime_when_the_sdk_corrects_its_clock()
    {
        // The SDK subtracts its own clock-skew-corrected time, not DateTime.UtcNow. In CI a corrected clock produced
        // X-Amz-Expires=301 for a five-minute link.
        AWSConfigs.ManualClockCorrection = TimeSpan.FromSeconds(-2);
        try
        {
            var url = Storage.CreateSignedUrl(NewKey(".pdf"), TimeSpan.FromMinutes(5));

            Assert.That(ExpiresSeconds(url), Is.EqualTo(300), url.Query);
        }
        finally
        {
            AWSConfigs.ManualClockCorrection = null;
        }
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

    [TestCase(0, TestName = "A_signed_url_without_lifetime_is_rejected")]
    [TestCase(-60, TestName = "A_signed_url_with_negative_lifetime_is_rejected")]
    [TestCase(3601, TestName = "A_signed_url_longer_than_the_configured_limit_is_rejected")]
    public void Signed_url_lifetimes_are_bounded(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Storage.CreateSignedUrl(NewKey(".pdf"), TimeSpan.FromSeconds(seconds)));
    }

    [TestCase("", TestName = "S3_an_empty_key_is_rejected")]
    [TestCase("/etc/passwd", TestName = "S3_an_absolute_key_is_rejected")]
    [TestCase("../outside.xml", TestName = "S3_a_key_that_climbs_out_is_rejected")]
    [TestCase("a/../../outside.xml", TestName = "S3_a_key_that_climbs_out_from_a_folder_is_rejected")]
    [TestCase("a/./b.xml", TestName = "S3_a_key_with_a_dot_segment_is_rejected")]
    [TestCase("a\\..\\b.xml", TestName = "S3_a_key_with_backslashes_is_rejected")]
    [TestCase("a//b.xml", TestName = "S3_a_key_with_an_empty_segment_is_rejected")]
    [TestCase("a/b.xml?x=1", TestName = "S3_a_key_with_a_query_is_rejected")]
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

    private static int ExpiresSeconds(Uri url) => int.Parse(
        url.Query.TrimStart('?').Split('&').Single(part => part.StartsWith("X-Amz-Expires=", StringComparison.Ordinal))["X-Amz-Expires=".Length..],
        System.Globalization.CultureInfo.InvariantCulture);

    private static string NewKey(string extension) => $"{Guid.NewGuid():N}/documents/{Guid.NewGuid():N}/{Guid.NewGuid():N}{extension}";

    private static MemoryStream Content(string text) => new(Encoding.UTF8.GetBytes(text));

    private static async Task<string> ReadAsync(string key)
    {
        await using var stream = await Storage.OpenReadAsync(key, CancellationToken.None);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}
