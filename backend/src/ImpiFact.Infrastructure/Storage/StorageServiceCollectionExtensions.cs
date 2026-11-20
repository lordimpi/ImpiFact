using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.Storage;

public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Registra el almacenamiento de archivos (<see cref="IBlobStorage"/>) con su validación al arrancar. El proveedor
    /// sale de <c>Storage:Provider</c>: <c>Local</c> (una carpeta), <c>S3</c> (un bucket de S3 o de un servicio
    /// compatible) o <c>AzureBlob</c> (un contenedor de Azure Blob Storage). Espera que <see cref="StorageOptions"/> ya
    /// esté registrado.
    /// </summary>
    public static IServiceCollection AddStorage(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<StorageOptions>, StorageOptionsValidator>());
        services.TryAddSingleton<IBlobStorage>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<StorageOptions>>().Value;
            return options.Provider switch
            {
                StorageOptions.S3Provider => S3BlobStorage.Create(options.S3, provider.GetService<S3Credentials>()?.Value),
                StorageOptions.AzureBlobProvider => AzureBlobStorage.Create(options.AzureBlob, provider.GetService<AzureBlobServiceClient>()),
                _ => ActivatorUtilities.CreateInstance<LocalBlobStorage>(provider),
            };
        });
        return services;
    }
}
