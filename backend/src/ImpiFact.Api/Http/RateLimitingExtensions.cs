using System.Globalization;
using System.Threading.RateLimiting;
using ImpiFact.Api.Identity;
using ImpiFact.Infrastructure.Identity;
using ImpiFact.Infrastructure.Options;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;

namespace ImpiFact.Api.Http;

/// <summary>
/// Límite de solicitudes por minuto (Arquitectura detallada, secciones 9.1, 11 y 19.1), en memoria de cada
/// instancia y con ventana fija de un minuto: uno por dirección IP y otro por API key que se aplican juntos, en ese
/// orden, y una
/// política más estricta para el inicio de sesión. Corre antes de la autenticación, así que la API key se cuenta
/// aunque todavía no se sepa si es válida; se identifica por su resumen SHA-256, nunca por el texto.
/// </summary>
public static class RateLimitingExtensions
{
    /// <summary>Política del endpoint de inicio de sesión: <c>.RequireRateLimiting(RateLimitingExtensions.LoginPolicy)</c>.</summary>
    public const string LoginPolicy = "login";

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    internal static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptionsService<RateLimitOptions>>((options, limits) =>
            {
                var perKey = limits.Value.PermitPerMinutePerKey;
                var perIp = limits.Value.PermitPerMinutePerIp;
                var login = limits.Value.LoginPerMinute;

                // Primero el límite por IP: si una IP ya lo agotó, la solicitud se rechaza sin crear un contador para la
                // llave que trae. Así, quien prueba miles de llaves inventadas desde una IP no llena la memoria con
                // contadores por llave.
                options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                    PartitionedRateLimiter.Create<HttpContext, string>(context =>
                        RateLimitPartition.GetFixedWindowLimiter(IpPartition(context), _ => FixedWindow(perIp))),
                    PartitionedRateLimiter.Create<HttpContext, string>(context => ApiKeyPartition(context) is { } key
                        ? RateLimitPartition.GetFixedWindowLimiter(key, _ => FixedWindow(perKey))
                        : RateLimitPartition.GetNoLimiter("no-api-key")));
                options.AddPolicy(LoginPolicy, context =>
                    RateLimitPartition.GetFixedWindowLimiter(IpPartition(context), _ => FixedWindow(login)));
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = WriteRejectionAsync;
            });
        return services;
    }

    private static FixedWindowRateLimiterOptions FixedWindow(int permits) =>
        new() { PermitLimit = permits, Window = Window, QueueLimit = 0, AutoReplenishment = true };

    /// <summary>
    /// Contador por llave solo para textos que pueden ser una API key: con el prefijo y sin pasar del largo máximo
    /// que acepta la autenticación. Un texto más largo nunca autentica y no merece un contador propio; cuenta solo
    /// en el límite por IP.
    /// </summary>
    private static string? ApiKeyPartition(HttpContext context) =>
        BearerToken.TryRead(context.Request, out var token)
        && token.Length <= ApiKeyAuthenticator.MaxLength
        && context.RequestServices.GetRequiredService<ApiKeyFormat>().IsApiKey(token)
            ? "key:" + CredentialHash.Sha256Hex(token)
            : null;

    private static string IpPartition(HttpContext context) =>
        "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");

    private static async ValueTask WriteRejectionAsync(OnRejectedContext rejected, CancellationToken ct)
    {
        var context = rejected.HttpContext;
        if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.Response.Headers[HeaderNames.RetryAfter] =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = { Status = StatusCodes.Status429TooManyRequests },
        });
    }
}
