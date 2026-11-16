using System.Text;
using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Domain.Documents;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Storage;

/// <summary>
/// Almacenamiento local de archivos (supuesto B3 de la fase 3): guarda y lee por clave relativa, reemplaza de forma
/// atómica, no deja temporales y rechaza toda clave que pueda salir de la carpeta raíz. No necesitan Docker.
/// </summary>
[TestFixture]
public sealed class LocalBlobStorageTests
{
    private StorageScratch scratch = null!;
    private ServiceProvider services = null!;
    private IBlobStorage storage = null!;

    [SetUp]
    public void SetUp()
    {
        scratch = new StorageScratch();
        services = StorageScratch.Build(scratch.LocalSettings());
        storage = services.GetRequiredService<IBlobStorage>();
    }

    [TearDown]
    public void TearDown()
    {
        services.Dispose();
        scratch.Dispose();
    }

    [Test]
    public async Task A_stored_file_is_read_back_with_the_same_bytes()
    {
        var key = await storage.PutAsync("company/documents/doc/file.xml", Content("<Invoice/>"), "application/xml", CancellationToken.None);
        var read = await ReadAsync(key);

        Assert.Multiple(() =>
        {
            Assert.That(key, Is.EqualTo("company/documents/doc/file.xml"));
            Assert.That(read, Is.EqualTo("<Invoice/>"));
            Assert.That(File.Exists(Path.Combine(scratch.StorageRoot, "company", "documents", "doc", "file.xml")), Is.True);
            Assert.That(storage.Provider, Is.EqualTo(StorageProvider.Local));
        });
    }

    [Test]
    public async Task Storing_again_under_the_same_key_replaces_the_content_and_leaves_no_temporary_files()
    {
        await storage.PutAsync("a/file.xml", Content("first version, longer"), "application/xml", CancellationToken.None);

        await storage.PutAsync("a/file.xml", Content("second"), "application/xml", CancellationToken.None);
        var read = await ReadAsync("a/file.xml");

        Assert.Multiple(() =>
        {
            Assert.That(read, Is.EqualTo("second"));
            Assert.That(Directory.GetFiles(Path.Combine(scratch.StorageRoot, "a")).Select(Path.GetFileName), Is.EqualTo(new[] { "file.xml" }));
        });
    }

    [Test]
    public void Reading_a_missing_key_reports_it_as_not_found()
    {
        var exception = Assert.ThrowsAsync<BlobNotFoundException>(() => storage.OpenReadAsync("missing/file.xml", CancellationToken.None));

        Assert.That(exception!.Key, Is.EqualTo("missing/file.xml"));
    }

    [Test]
    public async Task Deleting_removes_the_file_and_deleting_again_does_nothing()
    {
        await storage.PutAsync("a/file.xml", Content("x"), "application/xml", CancellationToken.None);

        await storage.DeleteAsync("a/file.xml", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(scratch.StorageRoot, "a", "file.xml")), Is.False);
            Assert.DoesNotThrowAsync(() => storage.DeleteAsync("a/file.xml", CancellationToken.None));
        });
    }

    [Test]
    public void Local_storage_cannot_sign_urls()
    {
        Assert.Throws<NotSupportedException>(() => storage.CreateSignedUrl("a/file.xml", TimeSpan.FromMinutes(5)));
    }

    [TestCase("", TestName = "An_empty_key_is_rejected")]
    [TestCase("/etc/passwd", TestName = "An_absolute_unix_key_is_rejected")]
    [TestCase("C:/Windows/win.ini", TestName = "A_key_with_a_drive_is_rejected")]
    [TestCase("../outside.xml", TestName = "A_key_that_climbs_out_is_rejected")]
    [TestCase("a/../../outside.xml", TestName = "A_key_that_climbs_out_from_a_folder_is_rejected")]
    [TestCase("a/./b.xml", TestName = "A_key_with_a_dot_segment_is_rejected")]
    [TestCase("a\\..\\b.xml", TestName = "A_key_with_backslashes_is_rejected")]
    [TestCase("a//b.xml", TestName = "A_key_with_an_empty_segment_is_rejected")]
    [TestCase("a/", TestName = "A_key_ending_in_a_slash_is_rejected")]
    [TestCase(".hidden", TestName = "A_key_starting_with_a_dot_is_rejected")]
    [TestCase("a/b.", TestName = "A_key_ending_in_a_dot_is_rejected")]
    [TestCase("a/CON", TestName = "A_windows_device_name_is_rejected")]
    [TestCase("a/nul.xml", TestName = "A_windows_device_name_with_extension_is_rejected")]
    [TestCase("a/b.xml\n", TestName = "A_key_with_a_trailing_new_line_is_rejected")]
    [TestCase("a/b c.xml", TestName = "A_key_with_a_space_is_rejected")]
    public void Keys_that_could_leave_the_storage_root_are_rejected_for_every_operation(string key)
    {
        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<ArgumentException>(() => storage.PutAsync(key, Content("x"), "application/xml", CancellationToken.None));
            Assert.ThrowsAsync<ArgumentException>(() => storage.OpenReadAsync(key, CancellationToken.None));
            Assert.ThrowsAsync<ArgumentException>(() => storage.DeleteAsync(key, CancellationToken.None));
        });
        Assert.That(Directory.EnumerateFileSystemEntries(scratch.Root, "*", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(scratch.StorageRoot, StringComparison.Ordinal)), Is.Empty, "Nothing is written outside the root.");
    }

    [Test]
    public void A_key_longer_than_the_storage_key_column_is_rejected()
    {
        Assert.ThrowsAsync<ArgumentException>(() => storage.PutAsync(new string('a', 501), Content("x"), "application/xml", CancellationToken.None));
    }

    private static MemoryStream Content(string text) => new(Encoding.UTF8.GetBytes(text));

    private async Task<string> ReadAsync(string key)
    {
        await using var stream = await storage.OpenReadAsync(key, CancellationToken.None);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}
