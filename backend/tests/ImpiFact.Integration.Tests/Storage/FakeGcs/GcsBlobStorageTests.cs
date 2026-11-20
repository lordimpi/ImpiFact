using System.Text;
using System.Web;
using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Domain.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ImpiFact.Integration.Tests.Storage.FakeGcs;

/// <summary>
/// Proveedor <c>Gcs</c> contra fake-gcs-server: guarda y lee por clave con su tipo de contenido, reemplaza, acepta
/// flujos que no se rebobinan, informa «no existe» con <see cref="BlobNotFoundException"/> solo cuando el bucket existe,
/// borra sin fallar si ya no existe, firma URL V4 de solo lectura con vigencia acotada, rechaza las mismas claves que
/// los demás proveedores, y la prueba de salud distingue un bucket que existe de uno que falta.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class GcsBlobStorageTests
{
    private static IBlobStorage Storage => FakeGcsStorage.Services.GetRequiredService<IBlobStorage>();

    [Test]
    public async Task A_stored_object_is_read_back_with_its_bytes_and_content_type()
    {
        var key = NewKey(".xml");

        var stored = await Storage.PutAsync(key, Content("<Invoice/>"), "application/xml", CancellationToken.None);
        var read = await ReadAsync(key);
        var metadata = await FakeGcsStorage.Client.GetObjectAsync(FakeGcsStorage.Bucket, key);

        Assert.Multiple(() =>
        {
            Assert.That(stored, Is.EqualTo(key));
            Assert.That(read, Is.EqualTo("<Invoice/>"));
            Assert.That(metadata.ContentType, Is.EqualTo("application/xml"));
            Assert.That(Storage.Provider, Is.EqualTo(StorageProvider.Gcs));
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

    // El bucket lo crea el despliegue: si falta, no es «el archivo no existe» sino un almacén mal configurado.
    [Test]
    public void A_missing_bucket_is_not_reported_as_a_missing_file()
    {
        using var services = FakeGcsStorage.BuildStorage(FakeGcsStorage.StorageSettings("missing-bucket"));
        var storage = services.GetRequiredService<IBlobStorage>();

        var read = Assert.ThrowsAsync<InvalidOperationException>(() => storage.OpenReadAsync(NewKey(".xml"), CancellationToken.None));
        var delete = Assert.ThrowsAsync<InvalidOperationException>(() => storage.DeleteAsync(NewKey(".xml"), CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(read!.Message, Is.EqualTo("The Cloud Storage bucket 'missing-bucket' does not exist."));
            Assert.That(delete!.Message, Is.EqualTo("The Cloud Storage bucket 'missing-bucket' does not exist."));
        });
    }

    [Test]
    public async Task The_readiness_check_is_healthy_with_the_bucket_and_unhealthy_without_it()
    {
        using var missing = FakeGcsStorage.BuildStorage(FakeGcsStorage.StorageSettings("missing-bucket"));

        var up = await StorageReadiness.CheckAsync(Storage);
        var down = await StorageReadiness.CheckAsync(missing.GetRequiredService<IBlobStorage>());

        Assert.Multiple(() =>
        {
            Assert.That(up.Status, Is.EqualTo(HealthStatus.Healthy));
            Assert.That(down.Status, Is.EqualTo(HealthStatus.Unhealthy));
        });
    }

    [Test]
    public void A_signed_url_is_a_v4_read_link_for_one_object_that_lasts_what_was_asked()
    {
        var key = NewKey(".pdf");

        var url = Storage.CreateSignedUrl(key, TimeSpan.FromMinutes(5));
        var query = HttpUtility.ParseQueryString(url.Query);

        Assert.Multiple(() =>
        {
            Assert.That(url.AbsolutePath, Is.EqualTo($"/{FakeGcsStorage.Bucket}/{key}"));
            Assert.That(query["X-Goog-Algorithm"], Is.EqualTo("GOOG4-RSA-SHA256"));
            Assert.That(query["X-Goog-Credential"], Does.StartWith(FakeGcsStorage.SignerEmail + "/"));
            Assert.That(query["X-Goog-Expires"], Is.EqualTo("300"));
            Assert.That(query["X-Goog-Signature"], Does.Match("^[0-9a-f]{512}$"), "An RSA-2048 signature in hex.");
        });
    }

    [TestCase(0, TestName = "Gcs_a_signed_url_without_lifetime_is_rejected")]
    [TestCase(-60, TestName = "Gcs_a_signed_url_with_negative_lifetime_is_rejected")]
    [TestCase(3601, TestName = "Gcs_a_signed_url_longer_than_the_configured_limit_is_rejected")]
    public void Signed_url_lifetimes_are_bounded(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Storage.CreateSignedUrl(NewKey(".pdf"), TimeSpan.FromSeconds(seconds)));
    }

    [TestCase("", TestName = "Gcs_an_empty_key_is_rejected")]
    [TestCase("/etc/passwd", TestName = "Gcs_an_absolute_key_is_rejected")]
    [TestCase("../outside.xml", TestName = "Gcs_a_key_that_climbs_out_is_rejected")]
    [TestCase("a/./b.xml", TestName = "Gcs_a_key_with_a_dot_segment_is_rejected")]
    [TestCase("a\\..\\b.xml", TestName = "Gcs_a_key_with_backslashes_is_rejected")]
    [TestCase("a//b.xml", TestName = "Gcs_a_key_with_an_empty_segment_is_rejected")]
    [TestCase("a/b.xml?x=1", TestName = "Gcs_a_key_with_a_query_is_rejected")]
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

    private static string NewKey(string extension) => $"{Guid.NewGuid():N}/documents/{Guid.NewGuid():N}/{Guid.NewGuid():N}{extension}";

    private static MemoryStream Content(string text) => new(Encoding.UTF8.GetBytes(text));

    private static async Task<string> ReadAsync(string key)
    {
        await using var stream = await Storage.OpenReadAsync(key, CancellationToken.None);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}
