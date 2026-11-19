using System.Security.Cryptography.X509Certificates;
using ImpiFact.Application.Abstractions.Dian;
using ImpiFact.Application.Abstractions.Observability;
using ImpiFact.Dian.Soap;

namespace ImpiFact.Infrastructure.Observability;

/// <summary>
/// Mide cada llamada al servicio web de la DIAN (<see cref="IDianWebService"/>): su duración y su resultado por operación
/// en <c>impifact.dian.call.duration</c>, y el resultado en <see cref="DianCallMonitor"/>, que alimenta el estado
/// «degradado» de la DIAN en <c>/health/ready</c>. No cambia lo que devuelve la llamada.
/// </summary>
internal sealed class InstrumentedDianWebService(IDianWebService inner, ImpiFactMetrics metrics, DianCallMonitor monitor) : IDianWebService
{
    public async Task<DianCall> CallAsync(DianSoapRequest request, string endpointUrl, X509Certificate2 certificate, CancellationToken ct)
    {
        var call = await inner.CallAsync(request, endpointUrl, certificate, ct);
        metrics.DianCall(request.OperationName, OutcomeName(call.Outcome), call.Duration);
        monitor.Record(call);
        return call;
    }

    public static string OutcomeName(DianCallOutcome outcome) => outcome switch
    {
        DianCallOutcome.Answered => "answered",
        DianCallOutcome.NotSent => "not_sent",
        _ => "no_answer",
    };
}
