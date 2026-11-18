using ImpiFact.Application.Abstractions.Companies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ImpiFact.Infrastructure.Companies;

public static class CompanyServiceCollectionExtensions
{
    /// <summary>Registra los adaptadores del módulo Company: repositorio, consultas, catálogos, URL de la DIAN y logo.</summary>
    public static IServiceCollection AddCompanies(this IServiceCollection services)
    {
        services.TryAddScoped<ICompanyRepository, EfCompanyRepository>();
        services.TryAddScoped<ICompanyReads, EfCompanyReads>();
        services.TryAddScoped<ICatalogLookup, EfCatalogLookup>();
        services.TryAddSingleton<IDianServiceUrls, DianServiceUrls>();
        services.TryAddScoped<ICompanyLogos, EfCompanyLogos>();
        return services;
    }
}
