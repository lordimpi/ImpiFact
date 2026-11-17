using System.Text.Json;
using System.Text.Json.Nodes;

namespace ImpiFact.Application.Abstractions.Messaging;

/// <summary>
/// Procesa los mensajes de un tipo de la bandeja de salida (outbox: la tabla <c>outbox_messages</c> donde la API
/// deja trabajo para el Worker; Arquitectura detallada, sección 8). El Worker solo toma los mensajes de los tipos que
/// tienen un handler registrado: los demás quedan pendientes hasta que se despliegue su handler.
/// </summary>
/// <remarks>
/// Se registra con ámbito por mensaje (<c>Scoped</c>): cada mensaje recibe su propio ámbito de servicios, con el
/// tenant (la empresa del mensaje) ya fijado y actor <see cref="ActorType.System"/>. Un mensaje puede procesarse
/// más de una vez (por ejemplo, si el Worker se cae después de terminar y antes de marcarlo hecho), así que el
/// handler debe tolerar repetirse sin duplicar efectos. Cualquier excepción programa un reintento con retroceso
/// (backoff: espera cada vez más larga entre intentos); <see cref="PermanentOutboxFailureException"/> lo marca como
/// fallido sin más intentos.
/// </remarks>
public interface IOutboxMessageHandler
{
    /// <summary>Tipo de mensaje que procesa (ver <see cref="OutboxMessageTypes"/>). Un solo handler por tipo.</summary>
    string MessageType { get; }

    Task HandleAsync(OutboxMessageContext message, CancellationToken ct);

    /// <summary>
    /// Se llama cuando el Worker se rinde con el mensaje (va a quedar fallido, sin más intentos), en un ámbito nuevo
    /// con el mismo tenant y antes de guardar el fallo y su notificación. Sirve para dejar en un estado final lo que
    /// el mensaje iba a cambiar; por ejemplo, el envío a la DIAN pasa el documento de <c>Queued</c> a <c>Failed</c> (o
    /// a <c>Unconfirmed</c> si la DIAN pudo recibirlo) y la consulta de estado programa otra consulta. Si falla, solo se
    /// registra en los logs y el mensaje queda fallido igual. Por defecto no hace nada.
    /// </summary>
    /// <remarks>
    /// Debe tolerar repetirse: si el proceso se cae después de llamarlo y antes de guardar el fallo, el mensaje se
    /// vuelve a tomar al vencer su bloqueo y se llama otra vez.
    /// </remarks>
    Task OnFailedAsync(OutboxMessageContext message, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Un mensaje de la bandeja de salida tal como lo recibe su handler.</summary>
/// <param name="MessageId">Id del mensaje en <c>outbox_messages</c>.</param>
/// <param name="CompanyId">Empresa del mensaje; nula para mensajes de la plataforma.</param>
/// <param name="Type">Tipo del mensaje.</param>
/// <param name="Payload">Datos del mensaje tal como se guardaron (JSON con nombres en snake_case).</param>
/// <param name="Attempt">Número de este intento, desde 1.</param>
/// <param name="PayloadSerializerOptions">Las mismas opciones de JSON con que se guardaron los datos.</param>
public sealed record OutboxMessageContext(
    Guid MessageId, Guid? CompanyId, string Type, JsonNode Payload, int Attempt, JsonSerializerOptions PayloadSerializerOptions)
{
    /// <summary>Convierte los datos del mensaje al tipo que espera el handler.</summary>
    /// <exception cref="PermanentOutboxFailureException">
    /// Si los datos no tienen la forma de <typeparamref name="TPayload"/>: reintentar no lo arreglaría.
    /// </exception>
    public TPayload GetPayload<TPayload>()
    {
        try
        {
            return Payload.Deserialize<TPayload>(PayloadSerializerOptions)
                ?? throw new PermanentOutboxFailureException($"The payload of outbox message {MessageId} is empty.");
        }
        catch (JsonException exception)
        {
            throw new PermanentOutboxFailureException(
                $"The payload of outbox message {MessageId} is not a valid {typeof(TPayload).Name}.", exception);
        }
    }
}

/// <summary>
/// Falla que no se arregla reintentando (por ejemplo, el documento del mensaje ya no existe): el Worker marca el
/// mensaje como fallido en el primer intento y avisa a la empresa.
/// </summary>
public sealed class PermanentOutboxFailureException : Exception
{
    public PermanentOutboxFailureException(string message)
        : base(message)
    {
    }

    public PermanentOutboxFailureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
