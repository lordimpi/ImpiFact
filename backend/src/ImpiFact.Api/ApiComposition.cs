using System.Net;
using ImpiFact.Api.Companies;
using ImpiFact.Api.Documents;
using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Api.Identity;
using ImpiFact.Api.Masters;
using ImpiFact.Api.Notifications;
using ImpiFact.Api.OpenApi;
using ImpiFact.Api.Plans;
using ImpiFact.Application;
using ImpiFact.Infrastructure;
using ImpiFact.Infrastructure.Identity;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Postgres;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;

namespace ImpiFact.Api;

/// <summary>Servicios y pipeline HTTP de la API (Arquitectura detallada, sección 9).</summary>
internal static class ApiComposition
{
    public static WebApplicationBuilder AddImpiFactApi(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        // Opciones validadas al arrancar, reloj, tenant y persistencia. Si falta un valor obligatorio (por ejemplo la
        // cadena de conexión, que llega por la variable de entorno Database__ConnectionString), la API no arranca.
        services.AddInfrastructure(configuration, new PostgresDatabaseProvider());
        services.AddIdentityInfrastructure();
        // Casos de uso de Application con sus decoradores; se llama una sola vez, después de registrar los adaptadores.
        services.AddApplication();
        services.AddValidatedOptions<RateLimitOptions>(configuration, RateLimitOptions.SectionName);
        services.AddValidatedOptions<CorsOriginsOptions>(configuration, CorsOriginsOptions.SectionName);

        services.AddHealthChecks();
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context => LegacyProblemFields.Apply(context.ProblemDetails, context.HttpContext));
        services.AddExceptionHandler<CredentialStoreUnavailableHandler>();
        services.AddExceptionHandler<InvalidRequestBodyHandler>();
        // Las rutas mínimas lanzan BadHttpRequestException ante un cuerpo ilegible en todos los ambientes (por defecto
        // solo en Development), así InvalidRequestBodyHandler responde siempre con el mismo formato.
        services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

        // Detrás de un proxy o ingress: la IP y el esquema reales vienen en X-Forwarded-For y X-Forwarded-Proto. Por
        // defecto solo se confía en la propia máquina (loopback); cada despliegue agrega sus proxies en la sección
        // ForwardedHeaders (KnownProxies y KnownNetworks), validada al arrancar.
        services.AddValidatedOptions<ForwardedHeadersTrustOptions>(configuration, ForwardedHeadersTrustOptions.SectionName);
        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptionsService<ForwardedHeadersTrustOptions>>((options, trust) =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var proxy in trust.Value.KnownProxies)
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }

            foreach (var network in trust.Value.KnownNetworks)
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });

        // CORS solo para los orígenes de Cors:Origins (validados al arrancar: nunca «*»). Con credenciales, para que el
        // panel reciba y envíe la cookie de sesión (PanelSession); sin orígenes configurados no se permite ninguno.
        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IOptionsService<CorsOriginsOptions>>((options, cors) =>
            options.AddDefaultPolicy(policy =>
            {
                policy.WithOrigins(cors.Value.Origins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .WithExposedHeaders(CorrelationIdMiddleware.HeaderName);
                if (cors.Value.Origins.Length > 0)
                {
                    policy.AllowCredentials();
                }
            }));

        services.AddApiRateLimiting();
        services.AddBearerAuthentication();
        services.AddScopeAuthorization();
        services.AddImpiFactOpenApi();

        services.AddSingleton<IEndpointModule, IdentityEndpoints>();
        services.AddSingleton<IEndpointModule, CompanyEndpoints>();
        services.AddSingleton<IEndpointModule, DocumentQueryEndpoints>();
        services.AddSingleton<IEndpointModule, DocumentIssueEndpoints>();
        services.AddSingleton<IEndpointModule, DocumentUtilityEndpoints>();
        services.AddSingleton<IEndpointModule, PlanEndpoints>();
        services.AddSingleton<IEndpointModule, NotificationEndpoints>();
        services.AddSingleton<IEndpointModule, MasterEndpoints>();
        return builder;
    }

    /// <summary>
    /// Orden del pipeline (sección 9.1): ForwardedHeaders → CorrelationId → ExceptionHandler → HTTPS/HSTS → CORS →
    /// RateLimiter → Authentication → TenantResolution → Authorization → endpoints. <c>UseRouting</c> va antes de
    /// CORS porque el límite de inicio de sesión y la autorización necesitan saber qué endpoint se va a ejecutar.
    /// </summary>
    public static WebApplication UseImpiFactPipeline(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseExceptionHandler();
        // Las respuestas de error sin cuerpo (401, 403, 404…) también salen como ProblemDetails.
        app.UseStatusCodePages();
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseRouting();
        app.UseCors();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseAuthorization();

        // Prueba de vida del proceso: responde 200 mientras la API esté arriba (Arquitectura, sección 13).
        app.MapHealthChecks("/health/live").AllowAnonymous();

        // Documentación OpenAPI y Scalar en /scalar solo en desarrollo y staging (sección 9.3). Solo describe las rutas
        // /api/v1, en español (OpenApiDocumentation).
        if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
        {
            app.MapOpenApi().AllowAnonymous();
            app.MapImpiFactApiReference().AllowAnonymous();
        }

        foreach (var module in app.Services.GetServices<IEndpointModule>())
        {
            module.MapEndpoints(app);
        }

        return app;
    }
}
