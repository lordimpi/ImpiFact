using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Identity;
using ImpiFact.Infrastructure;
using ImpiFact.Infrastructure.Identity;
using ImpiFact.Infrastructure.Postgres;

namespace ImpiFact.Api;

/// <summary>Servicios y pipeline HTTP de la API (Arquitectura detallada, sección 9).</summary>
internal static class ApiComposition
{
    public static WebApplicationBuilder AddImpiFactApi(this WebApplicationBuilder builder)
    {
        var services = builder.Services;

        // Opciones validadas al arrancar, reloj, tenant y persistencia. Si falta un valor obligatorio (por ejemplo la
        // cadena de conexión, que llega por la variable de entorno Database__ConnectionString), la API no arranca.
        services.AddInfrastructure(builder.Configuration, new PostgresDatabaseProvider());
        services.AddIdentityInfrastructure();

        services.AddHealthChecks();
        services.AddBearerAuthentication();
        services.AddScopeAuthorization();

        services.AddSingleton<IEndpointModule, IdentityEndpoints>();
        return builder;
    }

    /// <summary>Orden del pipeline (sección 9.1): Authentication → TenantResolution → Authorization → endpoints.</summary>
    public static WebApplication UseImpiFactPipeline(this WebApplication app)
    {
        app.UseRouting();
        app.UseAuthentication();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseAuthorization();

        // Prueba de vida del proceso: responde 200 mientras la API esté arriba (Arquitectura, sección 13).
        app.MapHealthChecks("/health/live").AllowAnonymous();

        foreach (var module in app.Services.GetServices<IEndpointModule>())
        {
            module.MapEndpoints(app);
        }

        return app;
    }
}
