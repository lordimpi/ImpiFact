using ImpiFact.Application.Abstractions.Companies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ImpiFact.Infrastructure.Companies;

public static class CompanyServiceCollectionExtensions
{
    /// <summary>Registra los adaptadores del módulo Company: repositorio, consultas, catálogos y URL de la DIAN.</summary>
    public static IServiceCollection AddCompanies(this IServiceCollection services)
    {
        services.TryAddScoped<ICompanyRepository, EfCompanyRepository>();
        services.TryAddScoped<ICompanyReads, EfCompanyReads>();
        services.TryAddScoped<ICatalogLookup, EfCatalogLookup>();
        services.TryAddSingleton<IDianServiceUrls, DianServiceUrls>();
        return services;
    }
}
