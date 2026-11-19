using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Infrastructure.Billing;
using ImpiFact.Infrastructure.Catalogs;
using ImpiFact.Infrastructure.Companies;
using ImpiFact.Infrastructure.Dashboard;
using ImpiFact.Infrastructure.Dian;
using ImpiFact.Infrastructure.Documents;
using ImpiFact.Infrastructure.Masters;
using ImpiFact.Infrastructure.Notifications;
using ImpiFact.Infrastructure.Observability;
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
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

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
    /// <exception cref="InvalidOperationException">
    /// Si falta <c>Database:Provider</c> o ningún motor disponible se llama así; el mensaje lista los disponibles.
    /// </exception>
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
        // Trazas y métricas del controlador de la base: quedan anotadas para el TracerProvider y el MeterProvider del host,
        // que solo existen si el host arma OpenTelemetry; sin él no se crea nada.
        services.ConfigureOpenTelemetryTracerProvider(provider.ConfigureTracing);
        services.ConfigureOpenTelemetryMeterProvider(provider.ConfigureMetrics);
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
        services.AddCatalogReads();
        services.AddDashboardReads();
        services.AddDianWebService(configuration);
        // Métricas de negocio y la medición de las llamadas a la DIAN (Arquitectura detallada, sección 12).
        services.AddBusinessMetrics();
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

    /// <summary>
    /// El motor que nombra <c>Database:Provider</c>, sin distinguir mayúsculas. No hay motor por defecto: sin el valor, o
    /// con uno que no está entre los disponibles, la aplicación no arranca y el mensaje dice cuáles hay.
    /// </summary>
    private static IDatabaseProvider SelectProvider(IConfiguration configuration, IDatabaseProvider[] providers)
    {
        var name = configuration.GetSection(DatabaseOptions.SectionName)[nameof(DatabaseOptions.Provider)];
        var available = $"Available providers: {string.Join(", ", providers.Select(provider => provider.Name))}.";
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException($"{DatabaseOptions.SectionName}:{nameof(DatabaseOptions.Provider)} is not set. {available}");
        }

        return providers.FirstOrDefault(provider => string.Equals(provider.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Database provider '{name}' is not available. {available}");
    }
}
