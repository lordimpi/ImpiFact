using Amazon.S3;
using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Protection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.Observability;

/// <summary>La base de datos responde: abre una conexión (sin consultar ni cambiar nada).</summary>
internal sealed class DatabaseHealthCheck(ImpiFactDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The database does not accept connections.");
}

/// <summary>
/// El almacenamiento de archivos responde: lee una clave que no existe (<see cref="ProbeKey"/>), sin escribir nada. Que
/// conteste «no existe» prueba que el servicio está y que las credenciales sirven.
/// </summary>
/// <remarks>
/// En S3, sin el permiso <c>s3:ListBucket</c> el servicio contesta <c>AccessDenied</c> en lugar de «no existe»: se reporta
/// como degradado (responde, pero no se pudo comprobar), no como caído.
/// </remarks>
internal sealed class StorageHealthCheck(IBlobStorage storage) : IHealthCheck
{
    public const string ProbeKey = "health/readiness-probe";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = await storage.OpenReadAsync(ProbeKey, cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (BlobNotFoundException)
        {
            return HealthCheckResult.Healthy();
        }
        catch (AmazonS3Exception exception) when (exception.ErrorCode == "AccessDenied")
        {
            return HealthCheckResult.Degraded("The storage answered but denied the probe; grant s3:ListBucket for an exact check.");
        }
    }
}

/// <summary>
/// El proveedor de secretos está listo. Con <c>LocalAesGcm</c>, la llave maestra se puede leer y tiene el tamaño correcto
/// (solo la lee). Los proveedores de la nube agregan su propia prueba.
/// </summary>
internal sealed class SecretsHealthCheck(IOptions<SecretsOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var secrets = options.Value;
        if (!string.Equals(secrets.Provider, SecretsOptions.LocalAesGcmProvider, StringComparison.Ordinal))
        {
            return Task.FromResult(HealthCheckResult.Healthy($"The {secrets.Provider} provider has no readiness probe yet."));
        }

        return Task.FromResult(LocalMasterKey.Validate(secrets.LocalAesGcm.MasterKeyFile) is null
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The local master key cannot be read."));
    }
}

/// <summary>
/// La DIAN, según las últimas llamadas de este proceso (<see cref="DianCallMonitor"/>): degradada si la mayoría falló,
/// nunca caída, porque sin la DIAN la API sigue recibiendo documentos y el Worker los reintenta.
/// </summary>
internal sealed class DianHealthCheck(DianCallMonitor monitor) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(monitor.IsDegraded()
            ? HealthCheckResult.Degraded("Most of the recent DIAN calls got no answer.")
            : HealthCheckResult.Healthy());
}
