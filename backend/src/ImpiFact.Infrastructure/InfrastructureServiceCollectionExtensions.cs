using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Infrastructure.Billing;
using ImpiFact.Infrastructure.Companies;
using ImpiFact.Infrastructure.Dian;
using ImpiFact.Infrastructure.Documents;
using ImpiFact.Infrastructure.Masters;
using ImpiFact.Infrastructure.Notifications;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Platform;
using ImpiFact.Infrastructure.Protection;
using ImpiFact.Infrastructure.Reads;
using ImpiFact.Infrastructure.Storage;
using ImpiFact.Infrastructure.Tenancy;
using ImpiFact.Infrastructure.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ImpiFact.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registra las opciones validadas al arrancar, el reloj, el contexto de tenant (tenant: cada empresa cliente
    /// con sus datos aislados) y la persistencia con el motor que nombra <c>Database:Provider</c>.
    /// </summary>
    /// <param name="providers">
    /// Motores disponibles. Los pasa quien arranca la aplicación (la API o el Migrator), porque Infrastructure no
    /// referencia a los proyectos de cada motor: son ellos los que referencian a Infrastructure.
    /// </param>
    /// <exception cref="InvalidOperationException">Si ningún motor disponible se llama como <c>Database:Provider</c>.</exception>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration, params IDatabaseProvider[] providers)
    {
        services.AddValidatedOptions<DatabaseOptions>(configuration, DatabaseOptions.SectionName);
        services.AddValidatedOptions<DianOptions>(configuration, DianOptions.SectionName);
        services.AddValidatedOptions<JwtOptions>(configuration, JwtOptions.SectionName);
        services.AddValidatedOptions<ApiKeyOptions>(configuration, ApiKeyOptions.SectionName);
        services.AddValidatedOptions<SecretsOptions>(configuration, SecretsOptions.SectionName);
        services.AddValidatedOptions<StorageOptions>(configuration, StorageOptions.SectionName);

        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddScoped<TenantContext>();
        services.TryAddScoped<ITenantContext>(serviceProvider => serviceProvider.GetRequiredService<TenantContext>());

        var provider = SelectProvider(configuration, providers);
        services.AddSingleton(provider);
        provider.AddServices(services);
        services.AddImpiFactDbContext<ImpiFactDbContext>();
        services.AddSecrets();
        services.AddCompanies();
        services.AddStorage();
        // La carpeta de certificados cifrados no puede quedar dentro del almacenamiento de documentos, ni al revés.
        services.AddSingleton(new ReservedDirectory("Secrets:LocalAesGcm:CertificateDirectory", configuration["Secrets:LocalAesGcm:CertificateDirectory"]));
        services.AddDocuments();
        services.AddBilling();
        services.AddPlatform();
        services.AddNotificationInbox();
        services.AddMasters();
        services.AddDianWebService(configuration);
        services.AddReads();
        return services;
    }

    /// <summary>
    /// Registra un contexto de EF Core (Entity Framework) con el motor elegido y deja la unidad de trabajo
    /// (<see cref="IUnitOfWork"/>) sobre ese contexto. <c>AddInfrastructure</c> la usa con
    /// <see cref="ImpiFactDbContext"/>; las pruebas, con un contexto hijo que solo tiene entidades de prueba.
    /// </summary>
    public static IServiceCollection AddImpiFactDbContext<TContext>(this IServiceCollection services)
        where TContext : ImpiFactDbContext
    {
        services.AddDbContext<TContext>((serviceProvider, options) =>
            serviceProvider.GetRequiredService<IDatabaseProvider>().ConfigureDbContext(serviceProvider, options));
        services.Replace(ServiceDescriptor.Scoped<IUnitOfWork>(serviceProvider =>
            new EfUnitOfWork(
                serviceProvider.GetRequiredService<TContext>(),
                serviceProvider.GetRequiredService<IDatabaseProvider>(),
                serviceProvider.GetServices<IPendingWrites>())));
        return services;
    }

    private static IDatabaseProvider SelectProvider(IConfiguration configuration, IDatabaseProvider[] providers)
    {
        var name = configuration.GetSection(DatabaseOptions.SectionName)[nameof(DatabaseOptions.Provider)];
        return providers.FirstOrDefault(provider => string.Equals(provider.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Database provider '{name}' is not available. Available providers: {string.Join(", ", providers.Select(provider => provider.Name))}.");
    }
}
