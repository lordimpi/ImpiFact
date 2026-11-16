using System.Security.Cryptography;

namespace ImpiFact.Integration.Tests.Protection;

/// <summary>
/// Carpeta temporal de una prueba, fuera del repositorio: guarda una llave maestra generada al azar en cada
/// ejecución y la carpeta de certificados cifrados. Se borra al liberarla. Nunca hay llaves reales en el código.
/// </summary>
public sealed class ProtectionScratch : IDisposable
{
    public ProtectionScratch()
    {
        Root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "impifact-tests", Guid.NewGuid().ToString("N"))).FullName;
        CertificateDirectory = Path.Combine(Root, "certificates");
        MasterKeyFile = WriteMasterKey("master.key", RandomNumberGenerator.GetBytes(32));
    }

    public string Root { get; }

    /// <summary>Archivo con 32 bytes al azar en base64, como el secreto montado en producción.</summary>
    public string MasterKeyFile { get; }

    public string CertificateDirectory { get; }

    /// <summary>Escribe otro archivo de llave con el contenido indicado, en base64.</summary>
    public string WriteMasterKey(string fileName, byte[] key)
    {
        var path = Path.Combine(Root, fileName);
        File.WriteAllText(path, Convert.ToBase64String(key) + "\n");
        return path;
    }

    /// <summary>Configuración del proveedor local que apunta a esta carpeta.</summary>
    public Dictionary<string, string?> LocalAesGcmSettings(string? masterKeyFile = null) => new()
    {
        ["Secrets:Provider"] = "LocalAesGcm",
        ["Secrets:LocalAesGcm:MasterKeyFile"] = masterKeyFile ?? MasterKeyFile,
        ["Secrets:LocalAesGcm:CertificateDirectory"] = CertificateDirectory,
    };

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
