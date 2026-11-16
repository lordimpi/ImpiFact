using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Storage;

/// <summary>
/// Carpeta temporal de una prueba de almacenamiento, fuera del repositorio; se borra al liberarla. Arma también los
/// servicios del almacenamiento con la configuración indicada.
/// </summary>
public sealed class StorageScratch : IDisposable
{
    public StorageScratch() =>
        Root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "impifact-tests", Guid.NewGuid().ToString("N"))).FullName;

    public string Root { get; }

    /// <summary>Raíz del almacén local dentro de la carpeta temporal (todavía no existe: el arranque la crea).</summary>
    public string StorageRoot => Path.Combine(Root, "storage");

    public Dictionary<string, string?> LocalSettings() => new()
    {
        ["Storage:Provider"] = "Local",
        ["Storage:Local:Root"] = StorageRoot,
    };

    public static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddValidatedOptions<StorageOptions>(configuration, StorageOptions.SectionName);
        services.AddSingleton(new ReservedDirectory("Secrets:LocalAesGcm:CertificateDirectory", configuration["Secrets:LocalAesGcm:CertificateDirectory"]));
        services.AddStorage();
        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
