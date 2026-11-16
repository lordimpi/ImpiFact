using ImpiFact.Application.Abstractions.Dian;
using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace ImpiFact.Infrastructure.Dian;

public static class DianServiceCollectionExtensions
{
    /// <summary>Nombre de la cadena de resiliencia (pipeline de Polly, la librería de reintentos) de la DIAN.</summary>
    internal const string PipelineName = "dian-soap";

    /// <summary>
    /// Registra el cliente del servicio web de la DIAN (<see cref="IDianWebService"/>) con su manejador de resiliencia
    /// (Microsoft.Extensions.Http.Resilience) y las opciones validadas de la sección «Resilience».
    /// </summary>
    /// <remarks>
    /// De afuera hacia adentro: plazo total (<see cref="ResilienceOptions.TotalSeconds"/>), reintentos con retroceso
    /// exponencial y jitter, <i>circuit breaker</i> (el corte que deja de llamar a la DIAN un rato cuando viene
    /// fallando; uno solo para todas las empresas, porque mide la salud del servicio) y plazo por intento
    /// (<see cref="DianOptions.TimeoutSeconds"/>). El <i>circuit breaker</i> queda por dentro de los reintentos, así
    /// cuenta cada intento fallido. El <see cref="HttpClient"/> no tiene plazo propio: lo ponen estas capas.
    /// </remarks>
    public static IServiceCollection AddDianWebService(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<ResilienceOptions>(configuration, ResilienceOptions.SectionName);
        services.TryAddSingleton<IDianWebService, DianSoapClient>();
        services.AddHttpClient(DianSoapClient.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .AddResilienceHandler(PipelineName, static (pipeline, context) =>
            {
                var resilience = context.ServiceProvider.GetRequiredService<IOptions<ResilienceOptions>>().Value;
                var dian = context.ServiceProvider.GetRequiredService<IOptions<DianOptions>>().Value;

                pipeline.AddTimeout(TimeSpan.FromSeconds(resilience.TotalSeconds));
                if (resilience.Retries > 0)
                {
                    pipeline.AddRetry(new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = resilience.Retries,
                        BackoffType = DelayBackoffType.Exponential,
                        UseJitter = true,
                        Delay = TimeSpan.FromSeconds(resilience.BaseDelaySeconds),
                        ShouldHandle = arguments => DianSoapClient.IsTransientAsync(arguments.Outcome, arguments.Context.CancellationToken),
                    });
                }

                pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 1.0,
                    MinimumThroughput = resilience.BreakerFailures,
                    SamplingDuration = TimeSpan.FromSeconds(resilience.BreakerSeconds),
                    BreakDuration = TimeSpan.FromSeconds(resilience.BreakDurationSeconds),
                    ShouldHandle = arguments => DianSoapClient.IsTransientAsync(arguments.Outcome, arguments.Context.CancellationToken),
                });
                pipeline.AddTimeout(TimeSpan.FromSeconds(dian.TimeoutSeconds));
            });
        return services;
    }
}
