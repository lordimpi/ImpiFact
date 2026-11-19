using Serilog.Core;
using Serilog.Events;

namespace ImpiFact.Infrastructure.Observability;

/// <summary>
/// Envuelve los destinos de los registros (consola, texto de las pruebas, OTLP) y les pasa cada evento con su excepción
/// sin datos sensibles (<see cref="RedactedException"/>): un enriquecedor no puede cambiar la excepción de un evento,
/// solo sus propiedades, que ya limpia <see cref="SensitiveDataRedactor"/>. El evento nuevo conserva la hora, el nivel,
/// la plantilla, las propiedades y la traza (<c>TraceId</c> y <c>SpanId</c>); si la excepción no tenía nada que tapar,
/// pasa el mismo evento.
/// </summary>
internal sealed class ExceptionRedactingSink(ILogEventSink inner) : ILogEventSink, IDisposable, IAsyncDisposable
{
    public void Emit(LogEvent logEvent)
    {
        var exception = logEvent.Exception is { } original ? RedactedException.From(original) : null;
        inner.Emit(ReferenceEquals(exception, logEvent.Exception)
            ? logEvent
            : new LogEvent(
                logEvent.Timestamp,
                logEvent.Level,
                exception,
                logEvent.MessageTemplate,
                logEvent.Properties.Select(property => new LogEventProperty(property.Key, property.Value)),
                logEvent.TraceId ?? default,
                logEvent.SpanId ?? default));
    }

    // Los destinos envueltos son de este envoltorio: al cerrar el registro, la consola y OTLP mandan lo pendiente.
    public void Dispose() => (inner as IDisposable)?.Dispose();

    public async ValueTask DisposeAsync()
    {
        switch (inner)
        {
            case IAsyncDisposable asynchronous:
                await asynchronous.DisposeAsync();
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }
}

/// <summary>
/// La excepción que se registra en lugar de una cuyo texto tenía un dato sensible: su <see cref="ToString"/> es el de
/// la original (tipo, mensaje, excepciones internas, también las de un <see cref="AggregateException"/>, y la traza de
/// la pila) pasado por <see cref="SensitiveDataRedactor.RedactText"/>, y lo mismo su mensaje y su traza.
/// </summary>
/// <remarks>
/// El tipo de .NET de este objeto no puede ser el de la original, así que donde un destino escribe el tipo aparte (el
/// atributo <c>exception.type</c> de OTLP) sale <c>RedactedException</c>; el tipo original queda en
/// <see cref="OriginalType"/> y al principio del texto (<c>exception.stacktrace</c> en OTLP, <c>@x</c> en JSON). Solo
/// pasa con las excepciones que tenían algo que tapar.
/// </remarks>
internal sealed class RedactedException : Exception
{
    private readonly string text;

    private RedactedException(Type originalType, string message, string text, string? stackTrace, int result, Exception? inner)
        : base(message, inner)
    {
        this.text = text;
        OriginalType = originalType.FullName ?? originalType.Name;
        StackTrace = stackTrace;
        HResult = result;
    }

    /// <summary>El nombre completo del tipo de la excepción original.</summary>
    public string OriginalType { get; }

    public override string? StackTrace { get; }

    public override string ToString() => text;

    /// <summary>La misma excepción si su texto no tenía nada que tapar; si no, una <see cref="RedactedException"/>.</summary>
    public static Exception From(Exception original)
    {
        if (Read(original) is not var (text, message, stackTrace))
        {
            return Unreadable(original);
        }

        return ReferenceEquals(SensitiveDataRedactor.RedactText(text), text)
            && ReferenceEquals(SensitiveDataRedactor.RedactText(message), message)
            && (stackTrace is null || ReferenceEquals(SensitiveDataRedactor.RedactText(stackTrace), stackTrace))
            ? original
            : Redact(original, text, message, stackTrace);
    }

    private static RedactedException Redact(Exception original, string text, string message, string? stackTrace)
    {
        var inner = original.InnerException is not { } innerException ? null
            : Read(innerException) is var (innerText, innerMessage, innerStackTrace) ? Redact(innerException, innerText, innerMessage, innerStackTrace)
            : Unreadable(innerException);
        return new(
            original.GetType(),
            SensitiveDataRedactor.RedactText(message),
            SensitiveDataRedactor.RedactText(text),
            stackTrace is null ? null : SensitiveDataRedactor.RedactText(stackTrace),
            original.HResult,
            inner);
    }

    /// <summary>El texto, el mensaje y la traza; nulo si la excepción (una propia) falla al darlos.</summary>
    private static (string Text, string Message, string? StackTrace)? Read(Exception exception)
    {
        try
        {
            return (exception.ToString(), exception.Message, exception.StackTrace);
        }
        catch (Exception)
        {
            // Un registro no debe fallar por una excepción que no se deja leer.
            return null;
        }
    }

    // Queda su tipo, sin un texto que no se pudo revisar.
    private static RedactedException Unreadable(Exception original)
    {
        var message = "[the exception text could not be read]";
        return new(original.GetType(), message, $"{original.GetType().FullName}: {message}", null, original.HResult, null);
    }
}
