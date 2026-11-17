using ImpiFact.Application.Features.Masters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ImpiFact.Infrastructure.Masters;

public static class MastersServiceCollectionExtensions
{
    /// <summary>Registra los almacenes de clientes y productos de la empresa (módulo Masters).</summary>
    public static IServiceCollection AddMasters(this IServiceCollection services)
    {
        services.TryAddScoped<ICustomerStore, CustomerStore>();
        services.TryAddScoped<IProductStore, ProductStore>();
        return services;
    }
}
