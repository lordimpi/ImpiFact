using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.Identity;

public static class IdentityServiceCollectionExtensions
{
    /// <summary>
    /// Registra las piezas de autenticación que no dependen de ASP.NET Core: API keys (generador, búsqueda con
    /// caché de 60 segundos, registro de uso en segundo plano, listado para el panel), llaves y emisor de JWT (JSON Web Token) y tokens de
    /// renovación. Se llama después de <c>AddInfrastructure</c>, que registra sus opciones. Solo la usa la API: el
    /// Migrator no firma tokens y no necesita la llave.
    /// </summary>
    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services)
    {
        services.AddIdentityPersistence();
        services.AddHybridCache();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<JwtOptions>, JwtSigningKeyValidation>());

        services.TryAddSingleton<JwtSigningKeys>();
        services.AddHostedService<JwtSigningKeysWarmup>();
        services.TryAddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.TryAddSingleton<IRefreshTokenFactory, RefreshTokenFactory>();
        services.TryAddSingleton<ISessionPolicy, SessionPolicy>();

        services.TryAddSingleton<ApiKeyFormat>();
        services.TryAddSingleton<ApiKeyCache>();
        services.TryAddSingleton<IApiKeyCacheInvalidator>(serviceProvider => serviceProvider.GetRequiredService<ApiKeyCache>());
        services.TryAddSingleton<ApiKeyUsageRecorder>();
        services.TryAddScoped<IApiKeyReads, EfApiKeyReads>();
        services.AddHostedService<ApiKeyUsageWriter>();
        services.TryAddScoped(serviceProvider => new ApiKeyAuthenticator(
            serviceProvider.GetRequiredService<IApiKeyStore>(),
            serviceProvider.GetRequiredService<ApiKeyCache>(),
            serviceProvider.GetRequiredService<ApiKeyUsageRecorder>(),
            serviceProvider.GetRequiredService<IClock>(),
            serviceProvider.GetRequiredService<ApiKeyFormat>(),
            serviceProvider.GetRequiredService<ILogger<ApiKeyAuthenticator>>()));
        return services;
    }

    /// <summary>
    /// Registra los adaptadores de Identity sobre las tablas (API keys, usuarios con sus roles y sus empresas, tokens de
    /// renovación y de restablecimiento de contraseña, y el correo de ese restablecimiento) y el resumidor de
    /// contraseñas, sin nada de JWT (JSON Web Token) ni de caché. La API los recibe con
    /// <see cref="AddIdentityInfrastructure"/>; el Worker, para el correo de restablecimiento; el Migrator puede usar
    /// solo esta parte para crear la primera llave de plataforma. Se llama después de <c>AddInfrastructure</c>, que
    /// registra el contexto de base de datos.
    /// </summary>
    public static IServiceCollection AddIdentityPersistence(this IServiceCollection services)
    {
        services.TryAddSingleton<IApiKeyGenerator, ApiKeyGenerator>();
        services.TryAddSingleton<CredentialStoreGuard>();
        services.TryAddScoped<IApiKeyStore, EfApiKeyStore>();
        services.TryAddScoped<IRefreshTokenStore, EfRefreshTokenStore>();
        services.TryAddScoped<IUserStore, EfUserStore>();
        services.TryAddScoped<IUserCompanyReads, EfUserCompanyReads>();
        services.TryAddScoped<ICompanyMemberStore, EfCompanyMemberStore>();
        services.TryAddScoped<IPasswordResetTokenStore, EfPasswordResetTokenStore>();
        services.TryAddScoped<IPasswordResetEmailQueue, PasswordResetEmailQueue>();
        services.TryAddSingleton<IPasswordResetTokenFactory, PasswordResetTokenFactory>();
        services.TryAddSingleton<IPasswordHasher, AspNetPasswordHasher>();
        return services;
    }

    /// <summary>
    /// Registra los enlaces al panel de los correos (<see cref="IPanelLinks"/>) con la sección «Panel» validada al
    /// arrancar: sin <c>Panel:BaseUrl</c> el proceso no arranca. Solo la llama el Worker, que es quien envía los correos.
    /// </summary>
    public static IServiceCollection AddPanelLinks(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<PanelOptions>(configuration, PanelOptions.SectionName);
        services.TryAddSingleton<IPanelLinks, PanelLinks>();
        return services;
    }
}
