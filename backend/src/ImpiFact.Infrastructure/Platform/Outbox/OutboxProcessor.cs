using System.Diagnostics;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Abstractions.Observability;
using ImpiFact.Infrastructure.Observability;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Persistence.Conversions;
using ImpiFact.Infrastructure.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImpiFact.Infrastructure.Platform.Outbox;

/// <summary>
/// Servicio en segundo plano del Worker que procesa la bandeja de salida (outbox: la tabla <c>outbox_messages</c> con
/// el trabajo pendiente; Arquitectura detallada, sección 8). En cada vuelta toma una tanda de mensajes vencidos de los
/// tipos que tienen handler (<see cref="IOutboxMessageHandler"/>), los procesa a la vez y registra el resultado de
/// cada uno: hecho, reintento con retroceso (backoff) o fallido.
/// </summary>
/// <remarks>
/// Cada mensaje corre en su propio ámbito de servicios con la empresa del mensaje como tenant y actor
/// <see cref="ActorType.System"/>. La falla de un mensaje (o de una empresa) nunca detiene a los demás ni al ciclo.
/// El sondeo es adaptativo: si la tanda vino llena, vuelve a consultar enseguida; si no, espera
/// <see cref="OutboxOptions.PollSeconds"/>. Los logs nombran el id y el tipo del mensaje, nunca sus datos.
/// </remarks>
internal sealed class OutboxProcessor(
    IServiceScopeFactory scopes,
    IOptions<OutboxOptions> options,
    OutboxWorkerId workerId,
    IClock clock,
    ImpiFactMetrics metrics,
    ILogger<OutboxProcessor> logger)
    : BackgroundService
{
    private IReadOnlyCollection<string>? handledTypes;

    /// <summary>
    /// Toma una tanda de mensajes y espera a que todos terminen.
    /// </summary>
    /// <returns>Cuántos mensajes tomó.</returns>
    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        var types = HandledTypes();
        if (types.Count == 0)
        {
            return 0;
        }

        IReadOnlyList<ClaimedOutboxMessage> messages;
        await using (var scope = OpenScope(companyId: null))
        {
            messages = await scope.ServiceProvider.GetRequiredService<OutboxQueue>()
                .ClaimAsync(workerId.Value, types, options.Value.BatchSize, ct);
        }

        // Cuánto esperó cada mensaje desde que debía correr (impifact.outbox.queue.lag): crece si el Worker no da abasto.
        var now = clock.UtcNow;
        foreach (var message in messages)
        {
            metrics.OutboxMessageTaken(message.Type, now - message.NextAttemptAt);
        }

        await Task.WhenAll(messages.Select(message => ProcessAsync(message, ct)));
        return messages.Count;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Fuera del try: dos handlers para el mismo tipo es un error de composición y detiene el Worker al arrancar.
        var types = HandledTypes();
        if (types.Count == 0)
        {
            logger.LogWarning("No outbox message handlers are registered; outbox processor {WorkerId} will not take any message.", workerId.Value);
        }
        else
        {
            logger.LogInformation(
                "Outbox processor {WorkerId} started for message types {MessageTypes}.", workerId.Value, string.Join(", ", types));
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var claimed = 0;
            try
            {
                claimed = await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Por ejemplo, la base no responde: se reintenta en la próxima vuelta.
                logger.LogError(exception, "Outbox processor {WorkerId} could not take a batch of messages.", workerId.Value);
            }

            if (claimed >= options.Value.BatchSize)
            {
                continue;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(options.Value.PollSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Procesa un mensaje dentro de un ámbito de los registros con su id, su tipo y su empresa
    /// (<c>OutboxMessageId</c>, <c>OutboxMessageType</c>, <c>CompanyId</c>): así cada línea que registre el handler
    /// dice de qué mensaje viene (Arquitectura detallada, sección 12).
    /// </summary>
    private async Task ProcessAsync(ClaimedOutboxMessage message, CancellationToken ct)
    {
        var scope = new Dictionary<string, object>(3)
        {
            ["OutboxMessageId"] = message.Id,
            ["OutboxMessageType"] = message.Type,
        };
        if (message.CompanyId is { } companyId)
        {
            scope["CompanyId"] = companyId;
        }

        // Un tramo de traza por mensaje (si hay quien escuche, por ejemplo OpenTelemetry con OTLP): las llamadas del
        // handler a la DIAN, a Gotenberg y a la base quedan dentro de él.
        using var activity = ImpiFactActivities.Source.StartActivity($"outbox {message.Type}", ActivityKind.Consumer);
        activity?.SetTag("messaging.system", "impifact.outbox");
        activity?.SetTag("messaging.operation.type", "process");
        activity?.SetTag("messaging.destination.name", message.Type);
        activity?.SetTag("messaging.message.id", message.Id.ToString());
        using (logger.BeginScope(scope))
        {
            await ProcessInScopeAsync(message, ct);
        }
    }

    private async Task ProcessInScopeAsync(ClaimedOutboxMessage message, CancellationToken ct)
    {
        Exception? failure = null;
        if (message.Attempts > options.Value.MaxAttempts)
        {
            // Solo pasa si las tomas anteriores no alcanzaron a registrar su resultado (por ejemplo, el mensaje
            // tumba el proceso): no se vuelve a ejecutar y queda fallido.
            failure = new PermanentOutboxFailureException(
                $"The message was taken {message.Attempts} times without recording an outcome; the worker probably stopped while processing it.");
        }
        else
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.HandlerTimeoutSeconds));
            try
            {
                await using var scope = OpenScope(message.CompanyId);
                await HandlerFor(scope, message).HandleAsync(ContextFor(message), timeout.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // El Worker se apaga: el mensaje queda en proceso y otra vuelta lo toma cuando venza su bloqueo.
                throw;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                failure = new TimeoutException(
                    $"The handler did not finish within {options.Value.HandlerTimeoutSeconds} seconds.");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        try
        {
            // Sin el token de apagado: si el handler ya terminó, su resultado se guarda aunque el Worker se esté
            // deteniendo; de lo contrario el mensaje se ejecutaría otra vez al vencer su bloqueo.
            await RecordAsync(message, failure, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Could not record the outcome of outbox message {MessageId} of type {MessageType}; it will be taken again when its lock expires.",
                message.Id, message.Type);
        }
    }

    private async Task RecordAsync(ClaimedOutboxMessage message, Exception? failure, CancellationToken ct)
    {
        // Ámbito nuevo: el contexto de base de datos del handler puede tener cambios a medias que no deben guardarse.
        await using var scope = OpenScope(companyId: null);
        var queue = scope.ServiceProvider.GetRequiredService<OutboxQueue>();
        if (failure is null)
        {
            if (await queue.CompleteAsync(workerId.Value, message, ct))
            {
                metrics.OutboxMessageProcessed(message.Type);
                logger.LogDebug("Outbox message {MessageId} of type {MessageType} processed.", message.Id, message.Type);
            }
            else
            {
                LogNotOwned(message);
            }

            return;
        }

        if (queue.IsFinal(message, failure))
        {
            // Primero el handler deja en un estado final lo que el mensaje iba a cambiar y después se guarda el fallo.
            // Si el proceso se cae entre los dos pasos, el mensaje sigue en proceso: otra vuelta lo toma al vencer su
            // bloqueo, pasado el máximo de intentos, y vuelve a avisar al handler. En el orden contrario, el mensaje
            // quedaría fallido para siempre y lo que iba a cambiar (por ejemplo, el documento) a medias.
            await NotifyFailedAsync(message);
        }

        switch (await queue.FailAsync(workerId.Value, message, failure, ct))
        {
            case OutboxFailureOutcome.Rescheduled:
                metrics.OutboxMessageFailed(message.Type, final: false);
                logger.LogWarning(
                    failure, "Outbox message {MessageId} of type {MessageType} failed on attempt {Attempt}; it will be retried.",
                    message.Id, message.Type, message.Attempts);
                break;
            case OutboxFailureOutcome.Failed:
                metrics.OutboxMessageFailed(message.Type, final: true);
                logger.LogError(
                    failure, "Outbox message {MessageId} of type {MessageType} failed on attempt {Attempt} and will not be retried.",
                    message.Id, message.Type, message.Attempts);
                break;
            default:
                LogNotOwned(message);
                break;
        }
    }

    /// <summary>
    /// Avisa al handler que el mensaje va a quedar fallido (<see cref="IOutboxMessageHandler.OnFailedAsync"/>), en un
    /// ámbito nuevo con el tenant del mensaje y el mismo plazo que un intento, antes de guardar el fallo. Su falla solo
    /// se registra: el mensaje queda fallido y con su notificación de todos modos.
    /// </summary>
    private async Task NotifyFailedAsync(ClaimedOutboxMessage message)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.Value.HandlerTimeoutSeconds));
        try
        {
            await using var scope = OpenScope(message.CompanyId);
            await HandlerFor(scope, message).OnFailedAsync(ContextFor(message), timeout.Token);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception, "The handler of failed outbox message {MessageId} of type {MessageType} could not record the failure.",
                message.Id, message.Type);
        }
    }

    private static IOutboxMessageHandler HandlerFor(AsyncServiceScope scope, ClaimedOutboxMessage message) =>
        scope.ServiceProvider.GetServices<IOutboxMessageHandler>()
            .First(candidate => string.Equals(candidate.MessageType, message.Type, StringComparison.Ordinal));

    private static OutboxMessageContext ContextFor(ClaimedOutboxMessage message) =>
        new(message.Id, message.CompanyId, message.Type, message.Payload, message.Attempts, JsonColumn.SerializerOptions);

    private void LogNotOwned(ClaimedOutboxMessage message) =>
        logger.LogWarning(
            "Outbox message {MessageId} of type {MessageType} was taken again by another instance before {WorkerId} closed it; its outcome was discarded.",
            message.Id, message.Type, workerId.Value);

    private AsyncServiceScope OpenScope(Guid? companyId)
    {
        var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(companyId, userId: null, ActorType.System);
        return scope;
    }

    /// <summary>Tipos con handler registrado; se calculan una vez, porque los registros no cambian en ejecución.</summary>
    /// <exception cref="InvalidOperationException">Si dos handlers dicen procesar el mismo tipo.</exception>
    private IReadOnlyCollection<string> HandledTypes()
    {
        if (handledTypes is not null)
        {
            return handledTypes;
        }

        using var scope = scopes.CreateScope();
        var types = scope.ServiceProvider.GetServices<IOutboxMessageHandler>().Select(handler => handler.MessageType).ToList();
        var duplicated = types.GroupBy(type => type, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        if (duplicated.Count > 0)
        {
            throw new InvalidOperationException(
                $"More than one outbox message handler is registered for: {string.Join(", ", duplicated)}.");
        }

        handledTypes = types;
        return handledTypes;
    }
}

/// <summary>
/// Nombre de esta instancia del Worker en la columna <c>locked_by</c>: el equipo, el proceso y un sufijo aleatorio,
/// para que dos procesos (incluso en el mismo equipo) nunca compartan nombre.
/// </summary>
internal sealed record OutboxWorkerId(string Value)
{
    private const int MaxLength = 100;

    public static OutboxWorkerId Create()
    {
        var value = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        return new OutboxWorkerId(value.Length <= MaxLength ? value : value[^MaxLength..]);
    }
}
