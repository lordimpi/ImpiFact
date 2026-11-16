using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.Storage;

public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Registra el almacenamiento de archivos (<see cref="IBlobStorage"/>) con su validación al arrancar. En la fase 3
    /// solo existe el proveedor local. Espera que <see cref="StorageOptions"/> ya esté registrado.
    /// </summary>
    public static IServiceCollection AddStorage(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<StorageOptions>, StorageOptionsValidator>());
        services.TryAddSingleton<IBlobStorage, LocalBlobStorage>();
        return services;
    }
}
