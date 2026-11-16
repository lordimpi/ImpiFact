using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.Identity;

public static class IdentityServiceCollectionExtensions
{
    /// <summary>
    /// Registra las piezas de autenticación que no dependen de ASP.NET Core: API keys (generador, búsqueda con
    /// caché de 60 segundos, registro de uso en segundo plano), llaves y emisor de JWT (JSON Web Token) y tokens de
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

        services.TryAddSingleton<ApiKeyFormat>();
        services.TryAddSingleton<ApiKeyCache>();
        services.TryAddSingleton<IApiKeyCacheInvalidator>(serviceProvider => serviceProvider.GetRequiredService<ApiKeyCache>());
        services.TryAddSingleton<ApiKeyUsageRecorder>();
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
    /// Registra los adaptadores de Identity sobre las tablas (API keys, usuarios con sus roles y tokens de
    /// renovación) y el resumidor de contraseñas, sin nada de JWT (JSON Web Token) ni de caché. La API los recibe
    /// con <see cref="AddIdentityInfrastructure"/>; el Migrator puede usar solo esta parte para crear la primera
    /// llave de plataforma. Se llama después de <c>AddInfrastructure</c>, que registra el contexto de base de datos.
    /// </summary>
    public static IServiceCollection AddIdentityPersistence(this IServiceCollection services)
    {
        services.TryAddSingleton<IApiKeyGenerator, ApiKeyGenerator>();
        services.TryAddScoped<IApiKeyStore, EfApiKeyStore>();
        services.TryAddScoped<IRefreshTokenStore, EfRefreshTokenStore>();
        services.TryAddScoped<IUserStore, EfUserStore>();
        services.TryAddSingleton<IPasswordHasher, AspNetPasswordHasher>();
        return services;
    }
}
