using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Dian;
using ImpiFact.Dian.Soap;

namespace ImpiFact.Infrastructure.Observability;

/// <summary>
/// Resultado de las últimas llamadas a la DIAN de este proceso, para reportar la DIAN como «degradada» en
/// <c>/health/ready</c> sin llamarla en cada consulta de salud (Arquitectura detallada, sección 12).
/// </summary>
/// <remarks>
/// Una llamada falla si no tuvo respuesta (no se envió, incluido el <i>circuit breaker</i> abierto, o no se recibió) o
/// si la DIAN respondió un error del servidor (HTTP 5xx) que no es un SOAP Fault: un SOAP Fault es la DIAN rechazando el
/// mensaje, no la DIAN caída. Solo guarda las últimas <see cref="Capacity"/> llamadas.
/// </remarks>
internal sealed class DianCallMonitor(IClock clock)
{
    /// <summary>Cuánto atrás se miran las llamadas.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    /// <summary>Llamadas dentro de <see cref="Window"/> que hacen falta para opinar.</summary>
    public const int MinimumCalls = 3;

    private const int Capacity = 50;

    private readonly Lock gate = new();
    private readonly Queue<(DateTimeOffset At, bool Failed)> calls = new();

    public void Record(DianCall call)
    {
        var failed = call.Outcome != DianCallOutcome.Answered
            || (call.HttpStatus >= 500 && call.Response is not DianSoapFaultResponse);
        lock (gate)
        {
            calls.Enqueue((clock.UtcNow, failed));
            while (calls.Count > Capacity)
            {
                calls.Dequeue();
            }
        }
    }

    /// <summary>Llamadas y fallas dentro de <see cref="Window"/>.</summary>
    public (int Calls, int Failures) Recent()
    {
        var since = clock.UtcNow - Window;
        lock (gate)
        {
            var recent = calls.Where(call => call.At >= since).ToList();
            return (recent.Count, recent.Count(call => call.Failed));
        }
    }

    /// <summary>
    /// Degradada si en <see cref="Window"/> hubo al menos <see cref="MinimumCalls"/> llamadas y más de la mitad falló.
    /// </summary>
    public bool IsDegraded()
    {
        var (count, failures) = Recent();
        return count >= MinimumCalls && failures * 2 > count;
    }
}
