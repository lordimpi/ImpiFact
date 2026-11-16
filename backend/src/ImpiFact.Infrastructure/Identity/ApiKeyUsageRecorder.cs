using System.Collections.Concurrent;
using System.Threading.Channels;
using ImpiFact.Application.Abstractions.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>
/// Anota el uso de una API key sin bloquear la solicitud (Arquitectura detallada, sección 9.2). No se llama a la
/// base dentro de la solicitud ni se lanza una tarea suelta («fire-and-forget»): esa tarea usaría el contexto de
/// base de datos de la solicitud, que se libera al responder. En su lugar, el uso entra a una cola en memoria y
/// <see cref="ApiKeyUsageWriter"/> lo guarda en segundo plano con su propio ámbito de servicios.
/// </summary>
/// <remarks>
/// <c>last_used_at</c> es informativo, así que se acepta perder datos: cada llave se anota como máximo una vez por
/// minuto, la cola tiene un tope y, si se llena o la API se apaga, los usos pendientes se descartan.
/// </remarks>
internal sealed class ApiKeyUsageRecorder
{
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(1);

    private const int Capacity = 10_000;

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastQueued = new();

    private readonly Channel<(Guid ApiKeyId, DateTimeOffset UsedAt)> _queue =
        Channel.CreateBounded<(Guid, DateTimeOffset)>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    public ChannelReader<(Guid ApiKeyId, DateTimeOffset UsedAt)> Reader => _queue.Reader;

    /// <summary>Encola el uso si la llave no se anotó en el último minuto. Nunca espera.</summary>
    public void Record(Guid apiKeyId, DateTimeOffset usedAt)
    {
        if (_lastQueued.TryGetValue(apiKeyId, out var last) && usedAt - last < MinimumInterval)
        {
            return;
        }

        _lastQueued[apiKeyId] = usedAt;
        _queue.Writer.TryWrite((apiKeyId, usedAt));
    }
}

/// <summary>Servicio en segundo plano que guarda en <c>api_keys.last_used_at</c> los usos que encola <see cref="ApiKeyUsageRecorder"/>.</summary>
internal sealed class ApiKeyUsageWriter(
    ApiKeyUsageRecorder recorder, IServiceScopeFactory scopes, ILogger<ApiKeyUsageWriter> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var (apiKeyId, usedAt) in recorder.Reader.ReadAllAsync(stoppingToken))
            {
                await WriteAsync(apiKeyId, usedAt, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // La API se está apagando: los usos pendientes se descartan.
        }
    }

    private async Task WriteAsync(Guid apiKeyId, DateTimeOffset usedAt, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IApiKeyStore>().MarkUsedAsync(apiKeyId, usedAt, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Un fallo aquí no debe detener la cola: el dato es informativo.
            logger.LogWarning(exception, "Could not record the last use of API key {ApiKeyId}.", apiKeyId);
        }
    }
}
