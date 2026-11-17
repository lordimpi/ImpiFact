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
    /// sale de <c>Storage:Provider</c>: <c>Local</c> (una carpeta) o <c>S3</c> (un bucket de S3 o de un servicio
    /// compatible). Espera que <see cref="StorageOptions"/> ya esté registrado.
    /// </summary>
    public static IServiceCollection AddStorage(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<StorageOptions>, StorageOptionsValidator>());
        services.TryAddSingleton<IBlobStorage>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<StorageOptions>>().Value;
            return string.Equals(options.Provider, StorageOptions.S3Provider, StringComparison.Ordinal)
                ? S3BlobStorage.Create(options.S3, provider.GetService<S3Credentials>()?.Value)
                : ActivatorUtilities.CreateInstance<LocalBlobStorage>(provider);
        });
        return services;
    }
}
