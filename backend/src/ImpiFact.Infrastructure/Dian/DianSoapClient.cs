using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Dian;
using ImpiFact.Dian.Soap;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace ImpiFact.Infrastructure.Dian;

/// <summary>
/// Cliente del servicio web de la DIAN (la autoridad tributaria de Colombia) sobre <see cref="HttpClient"/>
/// (Arquitectura detallada, sección 6.4): arma el sobre SOAP 1.2 (protocolo de mensajes XML de servicios web) firmado
/// con <see cref="DianSoapEnvelope"/>, lo envía con <c>Content-Type: application/soap+xml; charset=utf-8</c> y lee la
/// respuesta con <see cref="DianResponseReader"/>.
/// </summary>
/// <remarks>
/// <para>
/// Los reintentos, los tiempos de espera y el <i>circuit breaker</i> (el corte que deja de llamar a la DIAN un rato
/// cuando viene fallando) los pone el manejador de resiliencia registrado en <see cref="DianServiceCollectionExtensions"/>.
/// Todos los intentos envían el mismo sobre.
/// </para>
/// <para>
/// Cómo se informa cada falla: sin conexión (nombre no resuelto, conexión rechazada, TLS fallido) o con el
/// <i>circuit breaker</i> abierto, <see cref="DianCallOutcome.NotSent"/>: la DIAN no recibió nada. Tiempo agotado o
/// conexión cortada después de enviar, <see cref="DianCallOutcome.NoAnswer"/>: la DIAN pudo haberlo recibido. Los logs
/// nunca llevan el sobre (trae el documento completo).
/// </para>
/// </remarks>
internal sealed class DianSoapClient(IHttpClientFactory httpClients, IClock clock) : IDianWebService
{
    /// <summary>Nombre del <see cref="HttpClient"/> de la DIAN; las pruebas lo usan para cambiar el manejador final.</summary>
    internal const string HttpClientName = "Dian";

    private static readonly UTF8Encoding Utf8 = new(false);

    public async Task<DianCall> CallAsync(DianSoapRequest request, string endpointUrl, X509Certificate2 certificate, CancellationToken ct)
    {
        var envelope = DianSoapEnvelope.Build(request, endpointUrl, certificate, clock.UtcNow);
        var started = Stopwatch.GetTimestamp();
        try
        {
            // El HttpClient se pide en cada llamada, no al crear el cliente: así crear un handler del Worker (que lo
            // hace el procesador solo para leer su tipo de mensaje) no arma la cadena de resiliencia.
            var http = httpClients.CreateClient(HttpClientName);
            using var message = new HttpRequestMessage(HttpMethod.Post, endpointUrl)
            {
                Content = new StringContent(envelope, Utf8, DianSoapEnvelope.ContentType),
            };
            using var response = await http.SendAsync(message, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return new DianCall(
                DianCallOutcome.Answered, envelope, (int)response.StatusCode, body, DianResponseReader.Read(body), Elapsed(started), Failure: null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (BrokenCircuitException exception)
        {
            return Failed(DianCallOutcome.NotSent, envelope, started, $"The DIAN circuit breaker is open: {exception.Message}");
        }
        catch (HttpRequestException exception) when (IsBeforeSending(exception))
        {
            return Failed(DianCallOutcome.NotSent, envelope, started, $"Could not connect to the DIAN ({exception.HttpRequestError}): {exception.Message}");
        }
        catch (HttpRequestException exception)
        {
            return Failed(DianCallOutcome.NoAnswer, envelope, started, $"The DIAN connection failed ({exception.HttpRequestError}): {exception.Message}");
        }
        catch (TimeoutRejectedException exception)
        {
            return Failed(DianCallOutcome.NoAnswer, envelope, started, $"The DIAN did not answer in time: {exception.Message}");
        }
    }

    /// <summary>
    /// Errores transitorios que se reintentan y cuentan para el <i>circuit breaker</i>: los de
    /// <see cref="HttpClientResiliencePredicates.IsTransient(Outcome{HttpResponseMessage})"/> (red, tiempo agotado,
    /// HTTP 5xx, 408 y 429) menos el SOAP Fault, que es una respuesta de negocio de la DIAN aunque llegue con HTTP 500.
    /// </summary>
    internal static async ValueTask<bool> IsTransientAsync(Outcome<HttpResponseMessage> outcome, CancellationToken ct)
    {
        if (!HttpClientResiliencePredicates.IsTransient(outcome))
        {
            return false;
        }

        if (outcome.Result is not { } response)
        {
            return true;
        }

        // Leer el cuerpo lo deja en memoria: quien recibe la respuesta lo vuelve a leer sin problema.
        var body = await response.Content.ReadAsStringAsync(ct);
        return DianResponseReader.Read(body) is not DianSoapFaultResponse;
    }

    private static bool IsBeforeSending(HttpRequestException exception) => exception.HttpRequestError is
        HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError or HttpRequestError.SecureConnectionError
        or HttpRequestError.ProxyTunnelError;

    private static DianCall Failed(DianCallOutcome outcome, string envelope, long started, string failure) =>
        new(outcome, envelope, HttpStatus: null, Body: null, Response: null, Elapsed(started), failure);

    private static TimeSpan Elapsed(long started) => Stopwatch.GetElapsedTime(started);
}
