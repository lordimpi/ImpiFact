using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.Protection;

/// <summary>
/// Almacén de certificados de la instalación mínima (proveedor <c>LocalAesGcm</c>). Cada PKCS#12 (el archivo
/// .p12 o .pfx con el certificado y su llave privada) se guarda junto con su contraseña dentro de un sobre
/// AES-256-GCM (ver <see cref="AesGcmEnvelope"/>), en un archivo bajo <c>Secrets:LocalAesGcm:CertificateDirectory</c>:
/// <c>{carpeta}/{empresa}/{blob}.pfx.aesgcm</c>. La referencia que va a <c>certificates.secret_ref</c> es
/// <c>local:{empresa}/{blob}</c>.
/// </summary>
/// <remarks>
/// <para>
/// El archivo va en disco y no en una columna porque la base no guarda secretos ni blobs en filas calientes
/// (Modelo de datos detallado, regla 4); <c>secret_ref</c> es la «ruta cifrada» que describe el modelo.
/// </para>
/// <para>
/// Los datos asociados del sobre son la empresa y el blob, así un archivo copiado a otra empresa, o a otro
/// nombre, no abre. El certificado descifrado vive solo en memoria: se abre con
/// <see cref="X509KeyStorageFlags.EphemeralKeySet"/>, que no guarda la llave privada en el almacén del sistema,
/// y los búferes intermedios se borran.
/// </para>
/// </remarks>
internal sealed class LocalAesGcmCertificateStore(LocalMasterKey masterKey, IOptions<SecretsOptions> options) : ICertificateStore
{
    private const string Scheme = "local:";
    private const string FileExtension = ".pfx.aesgcm";
    private const string KeyPurpose = "impifact/certificates/v1";

    public async Task<string> SaveAsync(Guid companyId, byte[] pfx, string password, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pfx);
        ArgumentNullException.ThrowIfNull(password);

        var blobId = Guid.CreateVersion7();
        var envelope = Seal(companyId, blobId, pfx, password);
        var path = FilePath(companyId, blobId);
        CreatePrivateDirectory(Path.GetDirectoryName(path)!);

        // Se escribe a un archivo temporal y luego se renombra: nadie ve nunca un archivo a medio escribir.
        var temporary = path + ".tmp";
        var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        try
        {
            await using (var stream = new FileStream(temporary, fileOptions))
            {
                await stream.WriteAsync(envelope, ct);

                // Fuerza los bytes al disco antes de renombrar: tras un corte de luz no queda un archivo vacío.
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
        return $"{Scheme}{companyId:D}/{blobId:D}";
    }

    /// <summary>
    /// Crea la carpeta y sus padres. En Linux y macOS solo el dueño puede entrar (permisos 0700), así otros usuarios
    /// del servidor no pueden ni listar qué empresas tienen certificado.
    /// </summary>
    internal static void CreatePrivateDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
            return;
        }

        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public async Task<X509Certificate2> LoadAsync(Guid companyId, string secretRef, CancellationToken ct)
    {
        var blobId = ParseReference(companyId, secretRef);
        var path = FilePath(companyId, blobId);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The certificate does not exist.");
        }

        var envelope = await File.ReadAllBytesAsync(path, ct);
        var key = masterKey.DeriveKey(KeyPurpose);
        byte[]? plaintext = null;
        char[]? password = null;
        try
        {
            plaintext = AesGcmEnvelope.Open(key, envelope, AssociatedData(companyId, blobId));
            var passwordLength = BinaryPrimitives.ReadInt32BigEndian(plaintext);
            var passwordBytes = plaintext.AsSpan(sizeof(int), passwordLength);
            password = new char[Encoding.UTF8.GetCharCount(passwordBytes)];
            Encoding.UTF8.GetChars(passwordBytes, password);
            return X509CertificateLoader.LoadPkcs12(
                plaintext.AsSpan(sizeof(int) + passwordLength), password, X509KeyStorageFlags.EphemeralKeySet);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }

            if (password is not null)
            {
                Array.Clear(password);
            }
        }
    }

    public Task DeleteAsync(Guid companyId, string secretRef, CancellationToken ct)
    {
        var path = FilePath(companyId, ParseReference(companyId, secretRef));
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private byte[] Seal(Guid companyId, Guid blobId, byte[] pfx, string password)
    {
        // Contenido del sobre: largo de la contraseña (4 bytes) | contraseña en UTF-8 | PKCS#12.
        var passwordLength = Encoding.UTF8.GetByteCount(password);
        var plaintext = new byte[sizeof(int) + passwordLength + pfx.Length];
        var key = masterKey.DeriveKey(KeyPurpose);
        try
        {
            BinaryPrimitives.WriteInt32BigEndian(plaintext, passwordLength);
            Encoding.UTF8.GetBytes(password, plaintext.AsSpan(sizeof(int)));
            pfx.CopyTo(plaintext.AsSpan(sizeof(int) + passwordLength));
            return AesGcmEnvelope.Seal(key, plaintext, AssociatedData(companyId, blobId));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] AssociatedData(Guid companyId, Guid blobId) =>
        Encoding.UTF8.GetBytes($"impifact:certificate:v1:{companyId:D}:{blobId:D}");

    /// <summary>
    /// Lee <c>local:{empresa}/{blob}</c>. Ambas partes deben ser identificadores en su forma canónica, así la
    /// referencia nunca puede salirse de la carpeta (por ejemplo con <c>../</c>).
    /// </summary>
    private static Guid ParseReference(Guid companyId, string secretRef)
    {
        var parts = secretRef is not null && secretRef.StartsWith(Scheme, StringComparison.Ordinal)
            ? secretRef[Scheme.Length..].Split('/')
            : [];
        if (parts is not [var company, var blob]
            || !Guid.TryParseExact(company, "D", out var refCompanyId) || company != refCompanyId.ToString("D")
            || !Guid.TryParseExact(blob, "D", out var blobId) || blob != blobId.ToString("D"))
        {
            throw new ArgumentException("The certificate reference is not valid.", nameof(secretRef));
        }

        if (refCompanyId != companyId)
        {
            throw new ArgumentException("The certificate reference does not belong to the company.", nameof(secretRef));
        }

        return blobId;
    }

    private string FilePath(Guid companyId, Guid blobId) =>
        Path.Combine(Path.GetFullPath(options.Value.LocalAesGcm.CertificateDirectory), companyId.ToString("D"), blobId.ToString("D") + FileExtension);
}
