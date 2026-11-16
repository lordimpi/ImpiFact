using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ImpiFact.Application.Abstractions.Protection;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Protection;

/// <summary>
/// Almacén local de certificados con AES-256-GCM (un cifrado autenticado: además de ocultar el contenido, detecta
/// cualquier alteración). Cada archivo queda atado a su empresa con los datos asociados (associated data: datos
/// que no se cifran pero entran en la verificación), así un archivo copiado a otra empresa no abre. No necesitan
/// Docker.
/// </summary>
[TestFixture]
public sealed class LocalAesGcmCertificateStoreTests
{
    private static readonly Guid CompanyA = Guid.Parse("0192f5a0-0000-7000-8000-00000000000a");
    private static readonly Guid CompanyB = Guid.Parse("0192f5a0-0000-7000-8000-00000000000b");

    private ProtectionScratch scratch = null!;
    private ServiceProvider services = null!;
    private ICertificateStore store = null!;

    [SetUp]
    public void BuildStore()
    {
        scratch = new ProtectionScratch();
        services = ProtectionServices.Build(ProtectionServices.ValidSettings(scratch.LocalAesGcmSettings()));
        store = services.GetRequiredService<ICertificateStore>();
    }

    [TearDown]
    public void Cleanup()
    {
        services.Dispose();
        scratch.Dispose();
    }

    // En Linux las carpetas de certificados solo las abre el dueño (0700) y los archivos solo los lee él (0600).
    [Test]
    public async Task On_unix_only_the_owner_can_open_the_certificate_folders_and_files()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix permissions do not apply on Windows.");
            return;
        }

        const UnixFileMode OwnerOnlyFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

        var secretRef = await store.SaveAsync(CompanyA, TestPfx.Rsa(), TestPfx.Password, CancellationToken.None);
        var file = StoredFile(secretRef);

        // Se leen fuera de la lambda: el analizador de plataforma no ve la guarda de Windows dentro de ella.
        var rootMode = File.GetUnixFileMode(scratch.CertificateDirectory);
        var companyMode = File.GetUnixFileMode(Path.GetDirectoryName(file)!);
        var fileMode = File.GetUnixFileMode(file);

        Assert.Multiple(() =>
        {
            Assert.That(rootMode, Is.EqualTo(OwnerOnlyFolder));
            Assert.That(companyMode, Is.EqualTo(OwnerOnlyFolder));
            Assert.That(fileMode, Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        });
    }

    [Test]
    public async Task A_saved_certificate_loads_back_in_memory_with_its_rsa_private_key()
    {
        var pfx = TestPfx.Rsa();

        var secretRef = await store.SaveAsync(CompanyA, pfx, TestPfx.Password, CancellationToken.None);
        using var certificate = await store.LoadAsync(CompanyA, secretRef, CancellationToken.None);
        using var rsa = certificate.GetRSAPrivateKey();

        Assert.Multiple(() =>
        {
            Assert.That(secretRef, Does.StartWith($"local:{CompanyA:D}/"));
            Assert.That(certificate.Subject, Is.EqualTo("CN=ImpiFact Test Company, O=ImpiFact Pruebas, C=CO"));
            Assert.That(certificate.HasPrivateKey, Is.True);
            Assert.That(rsa, Is.Not.Null);
            var signature = rsa!.SignData("xml"u8.ToArray(), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            Assert.That(certificate.GetRSAPublicKey()!.VerifyData("xml"u8.ToArray(), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), Is.True);
        });
    }

    [Test]
    public async Task The_stored_file_contains_neither_the_pfx_nor_the_password()
    {
        var pfx = TestPfx.Rsa();

        var secretRef = await store.SaveAsync(CompanyA, pfx, TestPfx.Password, CancellationToken.None);
        var stored = await File.ReadAllBytesAsync(StoredFile(secretRef));

        Assert.Multiple(() =>
        {
            Assert.That(Contains(stored, pfx.AsSpan(0, 64)), Is.False);
            Assert.That(Contains(stored, Encoding.UTF8.GetBytes(TestPfx.Password)), Is.False);
            Assert.That(Directory.EnumerateFiles(scratch.Root, "*", SearchOption.AllDirectories).Count(), Is.EqualTo(2),
                "Only the master key and the encrypted certificate exist on disk.");
        });
    }

    [Test]
    public async Task Each_save_uses_a_fresh_reference_and_a_random_nonce()
    {
        var pfx = TestPfx.Rsa();

        var first = await store.SaveAsync(CompanyA, pfx, TestPfx.Password, CancellationToken.None);
        var second = await store.SaveAsync(CompanyA, pfx, TestPfx.Password, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(File.ReadAllBytes(StoredFile(second)).AsSpan(1, 12).ToArray(),
                Is.Not.EqualTo(File.ReadAllBytes(StoredFile(first)).AsSpan(1, 12).ToArray()));
        });
    }

    [Test]
    public async Task A_tampered_ciphertext_is_rejected()
    {
        var secretRef = await store.SaveAsync(CompanyA, TestPfx.Rsa(), TestPfx.Password, CancellationToken.None);
        var path = StoredFile(secretRef);
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[^1] ^= 0x01;
        await File.WriteAllBytesAsync(path, bytes);

        Assert.ThrowsAsync(Is.InstanceOf<CryptographicException>(),
            () => store.LoadAsync(CompanyA, secretRef, CancellationToken.None));
    }

    [Test]
    public async Task A_file_moved_to_another_company_is_rejected_by_the_associated_data()
    {
        var secretRef = await store.SaveAsync(CompanyA, TestPfx.Rsa(), TestPfx.Password, CancellationToken.None);
        var otherRef = secretRef.Replace(CompanyA.ToString("D"), CompanyB.ToString("D"), StringComparison.Ordinal);
        var otherPath = StoredFile(otherRef);
        Directory.CreateDirectory(Path.GetDirectoryName(otherPath)!);
        File.Copy(StoredFile(secretRef), otherPath);

        Assert.ThrowsAsync(Is.InstanceOf<CryptographicException>(),
            () => store.LoadAsync(CompanyB, otherRef, CancellationToken.None));
    }

    [Test]
    public async Task A_reference_of_another_company_is_rejected()
    {
        var secretRef = await store.SaveAsync(CompanyA, TestPfx.Rsa(), TestPfx.Password, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<ArgumentException>(() => store.LoadAsync(CompanyB, secretRef, CancellationToken.None));
            Assert.ThrowsAsync<ArgumentException>(() => store.DeleteAsync(CompanyB, secretRef, CancellationToken.None));
        });
        Assert.That(File.Exists(StoredFile(secretRef)), Is.True);
    }

    [TestCase("local:../../etc/passwd")]
    [TestCase("local:0192f5a0-0000-7000-8000-00000000000a/../x")]
    [TestCase("kv:0192f5a0-0000-7000-8000-00000000000a/0192f5a0-0000-7000-8000-000000000001")]
    [TestCase("")]
    public void A_malformed_reference_is_rejected(string secretRef) =>
        Assert.ThrowsAsync<ArgumentException>(() => store.LoadAsync(CompanyA, secretRef, CancellationToken.None));

    [Test]
    public async Task Delete_removes_the_encrypted_file_and_tolerates_a_missing_one()
    {
        var secretRef = await store.SaveAsync(CompanyA, TestPfx.Rsa(), TestPfx.Password, CancellationToken.None);

        await store.DeleteAsync(CompanyA, secretRef, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(StoredFile(secretRef)), Is.False);
            Assert.DoesNotThrowAsync(() => store.DeleteAsync(CompanyA, secretRef, CancellationToken.None));
            Assert.ThrowsAsync<FileNotFoundException>(() => store.LoadAsync(CompanyA, secretRef, CancellationToken.None));
        });
    }

    /// <summary>Ruta del archivo cifrado: <c>{carpeta}/{empresa}/{blob}.pfx.aesgcm</c>.</summary>
    private string StoredFile(string secretRef)
    {
        var parts = secretRef["local:".Length..].Split('/');
        return Path.Combine(scratch.CertificateDirectory, parts[0], parts[1] + ".pfx.aesgcm");
    }

    private static bool Contains(byte[] haystack, ReadOnlySpan<byte> needle) => haystack.AsSpan().IndexOf(needle) >= 0;
}
